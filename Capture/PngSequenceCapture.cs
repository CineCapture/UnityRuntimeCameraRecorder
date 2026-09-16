using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMediaRecorder
{
    // Captures a Unity camera as a numbered PNG image sequence without blocking the render loop.
    internal sealed class PngSequenceCapture : MonoBehaviour
    {
        private const int MaximumPendingReadbacks = 2;
        private const int MaximumQueuedFrames = 4;
        private Camera _camera;
        private PngSequenceSettings _settings;
        private RenderTexture _source;
        private RenderTexture _readbackTarget;
        private RenderTexture _previousTarget;
        private Coroutine _captureRoutine;
        private BlockingCollection<PngFrame> _frames;
        private Task _writerTask;
        private Exception _writerException;
        private long _captureIntervalTicks;
        private long _nextCaptureTimestamp;
        private bool _previousEnabled;
        private bool _ownsSource;
        private bool _ownsReadbackTarget;
        private bool _active;
        private int _pendingReadbacks;
        private int _capturedFrameCount;
        private int _droppedFrameCount;
        private int _nextFrameNumber;

        public int CapturedFrameCount => _capturedFrameCount;

        // Allocates render targets and starts asynchronous PNG production.
        public void StartCapture(Camera camera, PngSequenceSettings settings, RenderTexture preparedTarget)
        {
            _camera = camera;
            _settings = settings;
            Directory.CreateDirectory(settings.OutputDirectory);
            ValidatePreparedTarget(preparedTarget, settings.Width, settings.Height);

            _source = preparedTarget ?? CreateTarget(settings.Width, settings.Height, settings.AntiAliasingSamples);
            _ownsSource = preparedTarget == null;
            bool requiresReadbackTarget = _source.antiAliasing > 1 || settings.FlipVertically;
            _readbackTarget = requiresReadbackTarget ? CreateTarget(settings.Width, settings.Height, 1) : _source;
            _ownsReadbackTarget = requiresReadbackTarget;

            _frames = new BlockingCollection<PngFrame>(MaximumQueuedFrames);
            _writerTask = Task.Run(WriteFrames);
            _captureIntervalTicks = Math.Max(1L, (long)(Stopwatch.Frequency / settings.CapturesPerSecond));
            _nextCaptureTimestamp = Stopwatch.GetTimestamp() +
                (long)(Stopwatch.Frequency * settings.InitialDelaySeconds);
            _previousTarget = camera.targetTexture;
            _previousEnabled = camera.enabled;
            camera.targetTexture = _source;
            camera.enabled = true;
            _active = true;
            _captureRoutine = StartCoroutine(CaptureLoop());
            MediaRecorderLog.WriteInfo(
                $"Asynchronous PNG sequence capture started at {settings.CapturesPerSecond:0.###} image(s) per second: {settings.OutputDirectory}");
        }

        // Stops capture, drains GPU readbacks and waits for queued files to finish.
        public void StopCapture()
        {
            if (!_active && _source == null)
            {
                return;
            }

            _active = false;
            if (_captureRoutine != null)
            {
                StopCoroutine(_captureRoutine);
                _captureRoutine = null;
            }

            AsyncGPUReadback.WaitAllRequests();
            _frames?.CompleteAdding();
            _writerTask?.Wait();
            RestoreCameraAndReleaseTargets();
            _frames?.Dispose();
            _frames = null;
            _writerTask = null;

            MediaRecorderLog.WriteInfo(
                $"PNG sequence capture stopped: written={_capturedFrameCount}, dropped={_droppedFrameCount}.");
            if (_writerException != null)
            {
                throw new IOException("The PNG sequence writer failed.", _writerException);
            }
        }

        // Restores owned Unity resources if the host destroys this component early.
        private void OnDestroy()
        {
            try
            {
                StopCapture();
            }
            catch (Exception exception)
            {
                MediaRecorderLog.WriteError(exception);
            }
        }

        // Samples completed camera targets according to the wall-clock schedule.
        private IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (_active)
            {
                yield return endOfFrame;
                long timestamp = Stopwatch.GetTimestamp();
                if (timestamp < _nextCaptureTimestamp)
                {
                    continue;
                }

                ScheduleReadback(_nextFrameNumber++);
                _nextCaptureTimestamp += _captureIntervalTicks;
                if (_nextCaptureTimestamp < timestamp)
                {
                    _nextCaptureTimestamp = timestamp + _captureIntervalTicks;
                }
            }
        }

        // Resolves the rendered image and schedules a non-blocking GPU readback.
        private void ScheduleReadback(int frameNumber)
        {
            if (_pendingReadbacks >= MaximumPendingReadbacks || _frames.Count >= MaximumQueuedFrames)
            {
                _droppedFrameCount++;
                return;
            }

            if (_readbackTarget != _source)
            {
                if (_settings.FlipVertically)
                {
                    Graphics.Blit(_source, _readbackTarget, new Vector2(1f, -1f), new Vector2(0f, 1f));
                }
                else
                {
                    Graphics.Blit(_source, _readbackTarget);
                }
            }

            _pendingReadbacks++;
            AsyncGPUReadback.Request(
                _readbackTarget,
                0,
                TextureFormat.RGBA32,
                request => CompleteReadback(request, frameNumber));
        }

        // Copies a completed readback into the bounded background-writer queue.
        private void CompleteReadback(AsyncGPUReadbackRequest request, int frameNumber)
        {
            _pendingReadbacks--;
            if (request.hasError)
            {
                _droppedFrameCount++;
                MediaRecorderLog.WriteWarning("GPU readback failed for a PNG sequence frame.");
                return;
            }

            var frame = new PngFrame(frameNumber, request.GetData<byte>().ToArray());
            if (!_frames.IsAddingCompleted && _frames.TryAdd(frame))
            {
                return;
            }

            _droppedFrameCount++;
        }

        // Compresses and writes queued frames away from Unity's render thread.
        private void WriteFrames()
        {
            try
            {
                foreach (PngFrame frame in _frames.GetConsumingEnumerable())
                {
                    string path = Path.Combine(
                        _settings.OutputDirectory,
                        $"{_settings.FileNamePrefix}{frame.Number:D6}.png");
                    FastPngEncoder.WriteRgba32(path, frame.Pixels, _settings.Width, _settings.Height);
                    _capturedFrameCount++;
                }
            }
            catch (Exception exception)
            {
                _writerException = exception;
                while (_frames.TryTake(out _))
                {
                    _droppedFrameCount++;
                }
            }
        }

        // Restores the camera state and releases render targets owned by this capture.
        private void RestoreCameraAndReleaseTargets()
        {
            if (_camera != null)
            {
                _camera.enabled = _previousEnabled;
                _camera.targetTexture = _previousTarget;
            }

            if (_ownsReadbackTarget && _readbackTarget != null)
            {
                _readbackTarget.Release();
                Destroy(_readbackTarget);
            }

            if (_ownsSource && _source != null)
            {
                _source.Release();
                Destroy(_source);
            }

            _readbackTarget = null;
            _source = null;
            _camera = null;
        }

        // Creates an sRGB render texture suitable for Unity camera output.
        private static RenderTexture CreateTarget(int width, int height, int antiAliasingSamples)
        {
            var target = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            target.antiAliasing = antiAliasingSamples;
            target.Create();
            return target;
        }

        // Rejects a prepared target whose dimensions do not match the requested sequence.
        private static void ValidatePreparedTarget(RenderTexture target, int width, int height)
        {
            if (target != null && (target.width != width || target.height != height))
            {
                throw new ArgumentException("The prepared target does not match the PNG sequence dimensions.");
            }
        }

        // Carries one numbered RGBA frame from GPU readback to the PNG writer.
        private sealed class PngFrame
        {
            // Creates an immutable queued frame.
            public PngFrame(int number, byte[] pixels)
            {
                Number = number;
                Pixels = pixels;
            }

            public int Number { get; }
            public byte[] Pixels { get; }
        }
    }

    // Writes standards-compliant RGBA PNG files using fast lossless DEFLATE compression.
    internal static class FastPngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly uint[] CrcTable = CreateCrcTable();

        // Encodes one tightly packed RGBA32 buffer into a PNG file.
        public static void WriteRgba32(string path, byte[] pixels, int width, int height)
        {
            int rowBytes = checked(width * 4);
            if (pixels == null || pixels.Length != checked(rowBytes * height))
            {
                throw new ArgumentException("The RGBA buffer size does not match the PNG dimensions.", nameof(pixels));
            }

            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                output.Write(Signature, 0, Signature.Length);
                byte[] header = new byte[13];
                WriteUInt32BigEndian(header, 0, (uint)width);
                WriteUInt32BigEndian(header, 4, (uint)height);
                header[8] = 8;
                header[9] = 6;
                WriteChunk(output, "IHDR", header);
                WriteChunk(output, "IDAT", CompressScanlines(pixels, rowBytes, height));
                WriteChunk(output, "IEND", Array.Empty<byte>());
            }
        }

        // Compresses unfiltered PNG scanlines inside a zlib stream.
        private static byte[] CompressScanlines(byte[] pixels, int rowBytes, int height)
        {
            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);
                compressed.WriteByte(0x01);
                ulong adlerA = 1;
                ulong adlerB = 0;
                using (var deflate = new DeflateStream(
                    compressed,
                    System.IO.Compression.CompressionLevel.Fastest,
                    true))
                {
                    for (int row = 0; row < height; row++)
                    {
                        deflate.WriteByte(0);
                        adlerB += adlerA;
                        int offset = (height - 1 - row) * rowBytes;
                        deflate.Write(pixels, offset, rowBytes);
                        for (int index = 0; index < rowBytes; index++)
                        {
                            adlerA += pixels[offset + index];
                            adlerB += adlerA;
                        }

                        adlerA %= 65521;
                        adlerB %= 65521;
                    }
                }

                WriteUInt32BigEndian(compressed, (uint)((adlerB << 16) | adlerA));
                return compressed.ToArray();
            }
        }

        // Writes one PNG chunk with its length and CRC-32 checksum.
        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            WriteUInt32BigEndian(output, (uint)data.Length);
            output.Write(typeBytes, 0, typeBytes.Length);
            output.Write(data, 0, data.Length);
            uint crc = UpdateCrc(0xffffffffu, typeBytes);
            crc = UpdateCrc(crc, data);
            WriteUInt32BigEndian(output, crc ^ 0xffffffffu);
        }

        // Updates a PNG CRC-32 checksum for one byte array.
        private static uint UpdateCrc(uint crc, byte[] data)
        {
            foreach (byte value in data)
            {
                crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
            }

            return crc;
        }

        // Creates the lookup table used by PNG CRC-32 checksums.
        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                }

                table[index] = value;
            }

            return table;
        }

        // Writes one unsigned integer in PNG network byte order.
        private static void WriteUInt32BigEndian(Stream output, uint value)
        {
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }

        // Stores one unsigned integer in a byte array using PNG network byte order.
        private static void WriteUInt32BigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
