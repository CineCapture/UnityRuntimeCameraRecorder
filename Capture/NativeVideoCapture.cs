using System;
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

        public override string Name => "D3D11 NVENC";
        public override VideoStreamFormat StreamFormat => VideoStreamFormat.Hevc;

        // Allocates the GPU target and initializes the native NVENC encoder.
        public override void StartCapture(VideoCaptureContext context)
        {
            _context = context;
            _camera = context.Camera;
            ValidatePreparedTarget(context.PreparedTarget, context.Width, context.Height);
            if (context.FlipVertically)
            {
                _renderTarget = context.PreparedTarget ?? CreateTarget(context.Width, context.Height);
                _ownsRenderTarget = context.PreparedTarget == null;
            }

            _target = !context.FlipVertically && context.PreparedTarget != null
                ? context.PreparedTarget
                : CreateTarget(context.Width, context.Height);
            _ownsTarget = context.PreparedTarget == null || context.FlipVertically;
            _packetCallback = ReceivePacket;
            _renderEventFunction = D3D11NvencEncoderGetRenderEventFunction();
            _sessionId = D3D11NvencEncoderStart(
                    _target.GetNativeTexturePtr(),
                    context.Width,
                    context.Height,
                    context.MaximumFrameRate,
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
            Camera.onPostRender += HandleCameraPostRender;
            _camera.enabled = true;
        }

        // Stops NVENC capture and releases the GPU target.
        public override void StopCapture()
        {
            _active = false;
            Camera.onPostRender -= HandleCameraPostRender;
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
        private static RenderTexture CreateTarget(int width, int height)
        {
            var target = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
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

        // Submits this camera's completed Unity render to NVENC at the configured maximum rate.
        private void HandleCameraPostRender(Camera renderedCamera)
        {
            if (!_active || renderedCamera != _camera)
            {
                return;
            }

            long timestamp = Stopwatch.GetTimestamp();
            if (timestamp < _nextCaptureTimestamp)
            {
                return;
            }

            if (_renderTarget != null)
            {
                Graphics.Blit(
                    _renderTarget,
                    _target,
                    new Vector2(1f, -1f),
                    new Vector2(0f, 1f));
            }

            long timestampMicroseconds = timestamp * 1_000_000L / Stopwatch.Frequency;
            D3D11NvencEncoderQueueTexture(_sessionId, _target.GetNativeTexturePtr(), timestampMicroseconds);
            GL.IssuePluginEvent(_renderEventFunction, _sessionId);
            _nextCaptureTimestamp = timestamp + _captureIntervalTicks;
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
