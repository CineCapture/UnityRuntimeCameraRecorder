using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Landoria.FFmpegMediaWriter;

namespace Landoria.UnityMediaRecorder
{
    // Captures Unity frames through a direct Direct3D 11 to NVENC path.
    internal sealed class NativeVideoCapture : VideoCaptureBackend
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void PacketCallback(IntPtr data, int length, long timestampMicroseconds);

        private VideoCaptureContext _context;
        private Camera _camera;
        private RenderTexture _renderTarget;
        private RenderTexture _target;
        private RenderTexture _previousTarget;
        private PacketCallback _packetCallback;
        private IntPtr _renderEventFunction;
        private int _sessionId;
        private long _captureIntervalTicks;
        private long _nextCaptureTimestamp;
        private bool _previousEnabled;
        private bool _ownsRenderTarget;
        private bool _ownsTarget;
        private bool _active;
        private int _rejectedPackets;
        private Coroutine _captureCoroutine;

        public override string Name => "D3D11 NVENC";
        public override VideoStreamFormat StreamFormat => VideoStreamFormat.H264;

        // Returns whether the native DLL and its render callback can be loaded.
        internal static bool IsAvailable()
        {
            try
            {
                return D3D11NvencEncoderGetRenderEventFunction() != IntPtr.Zero;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        // Allocates the GPU target and initializes the native NVENC encoder.
        public override void StartCapture(VideoCaptureContext context)
        {
            _context = context;
            _camera = context.Camera;
            ValidatePreparedTarget(context.PreparedTarget, context.Width, context.Height);
            bool needsResolve = context.AntiAliasingSamples > 1;
            if (context.FlipVertically || needsResolve)
            {
                _renderTarget = context.PreparedTarget ?? CreateTarget(
                    context.Width,
                    context.Height,
                    context.AntiAliasingSamples);
                _ownsRenderTarget = context.PreparedTarget == null;
            }

            _target = !context.FlipVertically && !needsResolve && context.PreparedTarget != null
                ? context.PreparedTarget
                : CreateTarget(context.Width, context.Height, 1);
            _ownsTarget = context.PreparedTarget == null || context.FlipVertically || needsResolve;
            _packetCallback = ReceivePacket;
            _renderEventFunction = D3D11NvencEncoderGetRenderEventFunction();
            _sessionId = D3D11NvencEncoderStart(
                    _target.GetNativeTexturePtr(),
                    context.Width,
                    context.Height,
                    context.MaximumFrameRate,
                    context.EncodingQuality == VideoEncodingQuality.Balanced ? 4 : 5,
                    _packetCallback);
            if (_sessionId == 0)
            {
                throw new InvalidOperationException(GetNativeError(0));
            }

            _captureIntervalTicks = Math.Max(1L, Stopwatch.Frequency / context.MaximumFrameRate);
            _nextCaptureTimestamp = 0;
            _previousTarget = _camera.targetTexture;
            _previousEnabled = _camera.enabled;
            _camera.targetTexture = _renderTarget ?? _target;
            _active = true;
            _captureCoroutine = StartCoroutine(CaptureFramesAtEndOfFrame());
            _camera.enabled = true;
        }

        // Stops NVENC capture and releases the GPU target.
        public override void StopCapture()
        {
            _active = false;
            if (_captureCoroutine != null)
            {
                StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
            _camera.enabled = _previousEnabled;
            _camera.targetTexture = _previousTarget;
            _previousTarget = null;

            D3D11NvencEncoderStop(_sessionId);
            MediaRecorderLog.WriteInfo(
                $"Native capture frames: queued={D3D11NvencEncoderGetQueuedFrameCount(_sessionId)}, " +
                $"encoded={D3D11NvencEncoderGetEncodedFrameCount(_sessionId)}, " +
                $"dropped={D3D11NvencEncoderGetDroppedFrameCount(_sessionId)}.");
            D3D11NvencEncoderDestroy(_sessionId);
            _sessionId = 0;
            if (_rejectedPackets > 0)
            {
                MediaRecorderLog.WriteWarning($"Native capture rejected {_rejectedPackets} encoded packets during shutdown.");
            }

            _packetCallback = null;
            if (_renderTarget != null && _ownsRenderTarget)
            {
                _renderTarget.Release();
                Destroy(_renderTarget);
                _renderTarget = null;
            }

            if (_target != null && _ownsTarget)
            {
                _target.Release();
                Destroy(_target);
            }

            _target = null;
        }

        // Creates one sRGB render texture compatible with Unity camera output.
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

        // Rejects a prepared target whose dimensions do not match the recording.
        private static void ValidatePreparedTarget(RenderTexture target, int width, int height)
        {
            if (target != null && (target.width != width || target.height != height))
            {
                throw new ArgumentException("The prepared video target does not match the recording dimensions.");
            }
        }

        // Waits until every camera and Canvas has completed before sampling the final target.
        private IEnumerator CaptureFramesAtEndOfFrame()
        {
            while (_active)
            {
                yield return new WaitForEndOfFrame();
                CaptureCompletedFrame();
            }
        }

        // Submits the fully composed Unity frame to NVENC at the configured maximum rate.
        private void CaptureCompletedFrame()
        {
            if (!_active)
            {
                return;
            }

            long timestamp = Stopwatch.GetTimestamp();
            if (_nextCaptureTimestamp != 0 && timestamp < _nextCaptureTimestamp)
            {
                return;
            }

            if (_nextCaptureTimestamp == 0)
            {
                _nextCaptureTimestamp = timestamp;
            }

            if (_renderTarget != null)
            {
                if (_context.FlipVertically)
                {
                    Graphics.Blit(
                        _renderTarget,
                        _target,
                        new Vector2(1f, -1f),
                        new Vector2(0f, 1f));
                }
                else
                {
                    Graphics.Blit(_renderTarget, _target);
                }
            }

            long timestampMicroseconds = timestamp * 1_000_000L / Stopwatch.Frequency;
            D3D11NvencEncoderQueueTexture(_sessionId, _target.GetNativeTexturePtr(), timestampMicroseconds);
            GL.IssuePluginEvent(_renderEventFunction, _sessionId);
            _nextCaptureTimestamp += _captureIntervalTicks;
            if (_nextCaptureTimestamp < timestamp - _captureIntervalTicks)
            {
                _nextCaptureTimestamp = timestamp + _captureIntervalTicks;
            }
        }

        // Copies a compressed native packet into the bounded FFmpeg queue.
        private void ReceivePacket(IntPtr data, int length, long timestampMicroseconds)
        {
            byte[] packet = new byte[length];
            Marshal.Copy(data, packet, 0, length);
            if (!_context.WritePacket(packet, timestampMicroseconds))
            {
                _rejectedPackets++;
            }
        }

        // Reads the last detailed error exposed by the native encoder.
        private static string GetNativeError(int sessionId)
        {
            IntPtr pointer = D3D11NvencEncoderGetLastError(sessionId);
            return Marshal.PtrToStringAnsi(pointer) ?? "Unknown native NVENC error.";
        }

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Initializes the native encoder for a Unity texture.
        private static extern int D3D11NvencEncoderStart(
            IntPtr texture,
            int width,
            int height,
            int frameRate,
            int preset,
            PacketCallback callback);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Queues a texture for processing by the Unity render thread callback.
        private static extern void D3D11NvencEncoderQueueTexture(
            int sessionId,
            IntPtr texture,
            long timestampMicroseconds);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the native Unity render event callback.
        private static extern IntPtr D3D11NvencEncoderGetRenderEventFunction();

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Stops and flushes the native encoder.
        private static extern void D3D11NvencEncoderStop(int sessionId);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Removes a stopped native encoder session.
        private static extern void D3D11NvencEncoderDestroy(int sessionId);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the last native encoder error.
        private static extern IntPtr D3D11NvencEncoderGetLastError(int sessionId);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames accepted into the native surface pool.
        private static extern ulong D3D11NvencEncoderGetQueuedFrameCount(int sessionId);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames successfully encoded by NVENC.
        private static extern ulong D3D11NvencEncoderGetEncodedFrameCount(int sessionId);

        [DllImport("Landoria.D3D11NvencEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames skipped by the native surface pool.
        private static extern ulong D3D11NvencEncoderGetDroppedFrameCount(int sessionId);

    }
}
