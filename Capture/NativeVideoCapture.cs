using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using FFmpegMediaWriter;

namespace UnityMediaRecorder
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
        private RenderTexture _screenTarget;
        private ScreenCursorOverlay _screenCursorOverlay;
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
            _camera = context.Camera;
            bool needsResize = context.PreparedTarget != null && (context.PreparedTarget.width != context.Width || context.PreparedTarget.height != context.Height);
            bool needsResolve = (context.PreparedTarget?.antiAliasing ?? context.AntiAliasingSamples) > 1;
            if (context.FlipVertically || needsResolve || needsResize)
            {
                _renderTarget = context.PreparedTarget ?? CreateTarget(context.Width, context.Height, context.AntiAliasingSamples);
                _ownsRenderTarget = context.PreparedTarget == null;
            }

            _target = !context.FlipVertically && !needsResolve && !needsResize && context.PreparedTarget != null ? context.PreparedTarget : CreateTarget(context.Width, context.Height, 1);
            _ownsTarget = context.PreparedTarget == null || context.FlipVertically || needsResolve || needsResize;
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
            _previousTarget = _camera.targetTexture;
            _previousEnabled = _camera.enabled;
            if (!context.CaptureScreen)
            {
                _camera.targetTexture = _renderTarget ?? _target;
            }

            _active = true;
            _captureCoroutine = StartCoroutine(CaptureFramesAtEndOfFrame());
            if (!context.CaptureScreen)
            {
                _camera.enabled = true;
            }
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

            if (!_context.CaptureScreen)
            {
                _camera.enabled = _previousEnabled;
                _camera.targetTexture = _previousTarget;
            }

            _previousTarget = null;
            Direct3DVideoEncoderStop(_sessionId);
            _diagnosticsJson = Marshal.PtrToStringAnsi(Direct3DVideoEncoderGetTelemetry(_sessionId));
            MediaRecorderLog.WriteInfo("NATIVE_PIPELINE " + _diagnosticsJson);
            string nativeError = GetNativeError(_sessionId);
            if (!string.IsNullOrEmpty(nativeError))
            {
                MediaRecorderLog.WriteWarning("Native pipeline error: " + nativeError);
            }
            MediaRecorderLog.WriteInfo($"Native capture frames: queued={Direct3DVideoEncoderGetQueuedFrameCount(_sessionId)}, " + $"encoded={Direct3DVideoEncoderGetEncodedFrameCount(_sessionId)}, " + $"dropped={Direct3DVideoEncoderGetDroppedFrameCount(_sessionId)}.");
            Direct3DVideoEncoderDestroy(_sessionId);
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
            if (_screenTarget != null)
            {
                _screenTarget.Release();
                Destroy(_screenTarget);
                _screenTarget = null;
            }
            _screenCursorOverlay?.Dispose();
            _screenCursorOverlay = null;
        }

        // Creates one sRGB render texture compatible with Unity camera output.
        private static RenderTexture CreateTarget(int width, int height, int antiAliasingSamples)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.antiAliasing = antiAliasingSamples;
            target.Create();
            return target;
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

            if (_context.CaptureScreen)
            {
                if (_screenTarget == null || _screenTarget.width != Screen.width || _screenTarget.height != Screen.height)
                {
                    if (_screenTarget != null)
                    {
                        _screenTarget.Release();
                        Destroy(_screenTarget);
                    }

                    _screenTarget = CreateTarget(Screen.width, Screen.height, 1);
                }

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = null;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenTarget);
                if (_screenCursorOverlay == null)
                {
                    _screenCursorOverlay = new ScreenCursorOverlay();
                }
                _screenCursorOverlay.Draw(_screenTarget);
                RenderTexture.active = previousActive;
                Graphics.Blit(_screenTarget, _renderTarget ?? _target);
            }

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
