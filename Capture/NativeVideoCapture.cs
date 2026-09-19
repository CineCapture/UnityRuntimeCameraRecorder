using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using FFmpegMediaWriter;

namespace UnityRuntimeCameraRecorder
{
    // Captures Unity frames through a direct Direct3D 11 to NVENC path.
    internal sealed class NativeVideoCapture : VideoCaptureBackend
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void PacketCallback(IntPtr data, int length, long timestampMicroseconds);
        private VideoCaptureContext _context;
        private RenderTexture _renderTarget;
        private RenderTexture _target;
        private PacketCallback _packetCallback;
        private IntPtr _renderEventFunction;
        private int _sessionId;
        private long _captureIntervalTicks;
        private long _nextCaptureTimestamp;
        private bool _active;
        private int _rejectedPackets;
        private Coroutine _captureCoroutine;
        private RenderTexture _screenTarget;
        private ScreenCursorOverlay _screenCursorOverlay;
        private VideoSequenceCompositor _videoSequenceCompositor;
        private string _diagnosticsJson;
        public override string DiagnosticsJson => _diagnosticsJson;
        public override string Name => "D3D11 NVENC";
        private VideoStreamFormat _streamFormat = VideoStreamFormat.H264;
        public override VideoStreamFormat StreamFormat => _streamFormat;

        // Selects an immutable session codec before configuring the native encoder and writer.
        public override void ConfigureStreamFormat(VideoStreamFormat format)
        {
            if (_sessionId != 0)
            {
                throw new InvalidOperationException("The video codec cannot change during capture.");
            }
            if (format != VideoStreamFormat.H264 && format != VideoStreamFormat.Hevc)
            {
                throw new NotSupportedException("The native backend supports only H.264 and HEVC.");
            }
            _streamFormat = format;
        }

        // Returns whether the native DLL and its render callback can be loaded.
        internal static bool IsAvailable()
        {
            try
            {
                return Direct3DVideoEncoderGetRenderEventFunction() != IntPtr.Zero;
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
            _diagnosticsJson = null;
            _context = context;
            if (context.FlipVertically)
            {
                _renderTarget = CreateTarget(context.Width, context.Height);
            }

            _target = CreateTarget(context.Width, context.Height);
            _packetCallback = ReceivePacket;
            _renderEventFunction = Direct3DVideoEncoderGetRenderEventFunction();
            int preset = context.OptimizeForConcurrentEncoding ? 4 : context.QualityProfile.NativeEncodingPreset;
            _sessionId = Direct3DVideoEncoderStartWithConstantQPOptions(_target.GetNativeTexturePtr(), context.Width, context.Height, context.MaximumFrameRate, preset, (int)_streamFormat, context.QualityProfile.QuantizationParameter, context.OptimizeForConcurrentEncoding ? 1 : 0, _packetCallback);
            if (_sessionId == 0)
            {
                throw new InvalidOperationException(GetNativeError(0));
            }

            _captureIntervalTicks = Math.Max(1L, Stopwatch.Frequency / context.MaximumFrameRate);
            _nextCaptureTimestamp = 0;
            _videoSequenceCompositor = new VideoSequenceCompositor(context.VideoSequence, context.Width, context.Height, context.FlipVertically, Time.realtimeSinceStartup);

            _active = true;
            _captureCoroutine = StartCoroutine(CaptureFramesAtEndOfFrame());
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

            Direct3DVideoEncoderStop(_sessionId);
            _diagnosticsJson = Marshal.PtrToStringAnsi(Direct3DVideoEncoderGetTelemetry(_sessionId));
            RecorderLog.WriteInfo("NATIVE_PIPELINE " + _diagnosticsJson);
            string nativeError = GetNativeError(_sessionId);
            if (!string.IsNullOrEmpty(nativeError))
            {
                RecorderLog.WriteWarning("Native pipeline error: " + nativeError);
            }
            RecorderLog.WriteInfo($"Native capture frames: queued={Direct3DVideoEncoderGetQueuedFrameCount(_sessionId)}, " + $"encoded={Direct3DVideoEncoderGetEncodedFrameCount(_sessionId)}, " + $"dropped={Direct3DVideoEncoderGetDroppedFrameCount(_sessionId)}.");
            Direct3DVideoEncoderDestroy(_sessionId);
            _sessionId = 0;
            if (_rejectedPackets > 0)
            {
                RecorderLog.WriteWarning($"Native capture rejected {_rejectedPackets} encoded packets during shutdown.");
            }

            _packetCallback = null;
            if (_renderTarget != null)
            {
                ReleaseTarget(_renderTarget);
                _renderTarget = null;
            }

            if (_target != null)
            {
                ReleaseTarget(_target);
            }

            _target = null;
            if (_screenTarget != null)
            {
                ReleaseTarget(_screenTarget);
                _screenTarget = null;
            }
            _screenCursorOverlay?.Dispose();
            _screenCursorOverlay = null;
            _videoSequenceCompositor?.Dispose();
            _videoSequenceCompositor = null;
        }

        // Creates one sRGB render texture compatible with Unity camera output.
        private static RenderTexture CreateTarget(int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            return target;
        }

        // Releases an owned target without leaving it bound as the active render surface.
        private static void ReleaseTarget(RenderTexture target)
        {
            if (RenderTexture.active == target)
            {
                RenderTexture.active = null;
            }

            target.Release();
            Destroy(target);
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

            if (_videoSequenceCompositor.RequiresScreen)
            {
                CaptureScreenFrame();
            }
            _videoSequenceCompositor.Render(_renderTarget ?? _target, _screenTarget, Time.realtimeSinceStartup);

            if (_renderTarget != null)
            {
                if (_context.FlipVertically)
                {
                    Graphics.Blit(_renderTarget, _target, new Vector2(1f, -1f), new Vector2(0f, 1f));
                }
                else
                {
                    Graphics.Blit(_renderTarget, _target);
                }
            }

            long timestampMicroseconds = timestamp * 1_000_000L / Stopwatch.Frequency;
            Direct3DVideoEncoderQueueTexture(_sessionId, _target.GetNativeTexturePtr(), timestampMicroseconds);
            GL.IssuePluginEvent(_renderEventFunction, _sessionId);
            _nextCaptureTimestamp += _captureIntervalTicks;
            if (_nextCaptureTimestamp < timestamp - _captureIntervalTicks)
            {
                _nextCaptureTimestamp = timestamp + _captureIntervalTicks;
            }
        }

        // Captures the completed application frame and overlays the visible system cursor.
        private void CaptureScreenFrame()
        {
            if (_screenTarget == null || _screenTarget.width != Screen.width || _screenTarget.height != Screen.height)
            {
                if (_screenTarget != null)
                {
                    _screenTarget.Release();
                    Destroy(_screenTarget);
                }
                _screenTarget = CreateTarget(Screen.width, Screen.height);
            }
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = null;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenTarget);
            _screenCursorOverlay ??= new ScreenCursorOverlay();
            _screenCursorOverlay.Draw(_screenTarget);
            RenderTexture.active = previousActive;
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
            IntPtr pointer = Direct3DVideoEncoderGetLastError(sessionId);
            return Marshal.PtrToStringAnsi(pointer) ?? "Unknown native NVENC error.";
        }

        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Initializes the native encoder for a Unity texture.
        private static extern int Direct3DVideoEncoderStartWithConstantQP(IntPtr texture, int width, int height, int frameRate, int codec, int quantizationParameter, PacketCallback callback);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Initializes CQP encoding with explicit concurrency-aware NVENC options.
        private static extern int Direct3DVideoEncoderStartWithConstantQPOptions(IntPtr texture, int width, int height, int frameRate, int preset, int codec, int quantizationParameter, int concurrentEncoding, PacketCallback callback);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Queues a texture for processing by the Unity render thread callback.
        private static extern void Direct3DVideoEncoderQueueTexture(int sessionId, IntPtr texture, long timestampMicroseconds);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the native Unity render event callback.
        private static extern IntPtr Direct3DVideoEncoderGetRenderEventFunction();
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Stops and flushes the native encoder.
        private static extern void Direct3DVideoEncoderStop(int sessionId);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Removes a stopped native encoder session.
        private static extern void Direct3DVideoEncoderDestroy(int sessionId);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the last native encoder error.
        private static extern IntPtr Direct3DVideoEncoderGetLastError(int sessionId);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames accepted into the native surface pool.
        private static extern ulong Direct3DVideoEncoderGetQueuedFrameCount(int sessionId);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames successfully encoded by NVENC.
        private static extern ulong Direct3DVideoEncoderGetEncodedFrameCount(int sessionId);
        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the number of frames skipped by the native surface pool.
        private static extern ulong Direct3DVideoEncoderGetDroppedFrameCount(int sessionId);

        [DllImport("Direct3DVideoEncoder", CallingConvention = CallingConvention.StdCall)]
        // Retrieves the final native pipeline counters and CPU stage timings.
        private static extern IntPtr Direct3DVideoEncoderGetTelemetry(int sessionId);
    }
}
