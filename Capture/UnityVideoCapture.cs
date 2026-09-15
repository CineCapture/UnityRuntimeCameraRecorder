using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Landoria.FFmpegMediaWriter;

namespace Landoria.UnityMediaRecorder
{
    // Captures rendered Unity frames asynchronously and forwards them to FFmpeg.
    internal sealed class UnityVideoCapture : VideoCaptureBackend
    {
        private const int MaximumPendingReadbacks = 2;
        private VideoCaptureContext _context;
        private Camera _camera;
        private RenderTexture _source;
        private RenderTexture _target;
        private RenderTexture _previousTarget;
        private long _captureIntervalTicks;
        private long _nextCaptureTimestamp;
        private int _pendingReadbacks;
        private bool _previousEnabled;
        private bool _ownsSource;
        private bool _ownsTarget;
        private bool _active;
        private bool _flipVertically;

        public override string Name => "Unity GPU readback";
        public override VideoStreamFormat StreamFormat => VideoStreamFormat.RawRgba;

        // Allocates render targets and starts capture at the requested maximum rate.
        public override void StartCapture(VideoCaptureContext context)
        {
            _context = context;
            _camera = context.Camera;
            _flipVertically = context.FlipVertically;
            ValidatePreparedTarget(context.PreparedTarget, context.Width, context.Height);
            if (Screen.width != context.Width || Screen.height != context.Height || _flipVertically)
            {
                _source = context.PreparedTarget ?? CreateTarget(Screen.width, Screen.height);
                _ownsSource = context.PreparedTarget == null;
            }

            _target = _source == null && context.PreparedTarget != null
                ? context.PreparedTarget
                : CreateTarget(context.Width, context.Height);
            _ownsTarget = context.PreparedTarget == null || _source != null;
            _captureIntervalTicks = Math.Max(1L, Stopwatch.Frequency / context.MaximumFrameRate);
            _nextCaptureTimestamp = 0;
            _previousTarget = _camera.targetTexture;
            _previousEnabled = _camera.enabled;
            _camera.targetTexture = _source ?? _target;
            _active = true;
            Camera.onPostRender += HandleCameraPostRender;
            _camera.enabled = true;
        }

        // Stops capture and releases all allocated render textures.
        public override void StopCapture()
        {
            _active = false;
            Camera.onPostRender -= HandleCameraPostRender;
            _camera.enabled = _previousEnabled;
            _camera.targetTexture = _previousTarget;
            _previousTarget = null;

            if (_target != null && _ownsTarget)
            {
                _target.Release();
                Destroy(_target);
            }

            _target = null;

            if (_source != null && _ownsSource)
            {
                _source.Release();
                Destroy(_source);
                _source = null;
            }

            _source = null;
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

        // Schedules readback after this camera has been rendered by Unity.
        private void HandleCameraPostRender(Camera renderedCamera)
        {
            if (!_active || renderedCamera != _camera || _pendingReadbacks >= MaximumPendingReadbacks)
            {
                return;
            }

            long timestamp = Stopwatch.GetTimestamp();
            if (timestamp < _nextCaptureTimestamp)
            {
                return;
            }

            CaptureFrame();
            _nextCaptureTimestamp = timestamp + _captureIntervalTicks;
        }

        // Copies the completed render when needed and requests GPU readback.
        private void CaptureFrame()
        {
            if (_source != null)
            {
                if (_flipVertically)
                {
                    Graphics.Blit(
                        _source,
                        _target,
                        new Vector2(1f, -1f),
                        new Vector2(0f, 1f));
                }
                else
                {
                    Graphics.Blit(_source, _target);
                }
            }
            _pendingReadbacks++;
            AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32, CompleteReadback);
        }

        // Queues a completed GPU readback for delivery to FFmpeg.
        private void CompleteReadback(AsyncGPUReadbackRequest request)
        {
            _pendingReadbacks--;
            if (!_active || request.hasError)
            {
                return;
            }

            try
            {
                _context.WriteFrame(request.GetData<byte>().ToArray());
            }
            catch (Exception exception)
            {
                MediaRecorderLog.WriteError(exception);
            }
        }
    }
}
