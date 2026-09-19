using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMediaRecorder
{
    // Renders a timed camera sequence into one GPU texture with crossfade transitions.
    internal sealed class CameraSequenceCompositor : IDisposable
    {
        private readonly CameraSequenceSettings _settings;
        private readonly System.Random _random;
        private readonly List<CameraState> _cameraStates = new List<CameraState>();
        private readonly RenderTexture _firstTarget;
        private readonly RenderTexture _secondTarget;
        private readonly RenderTexture _firstResolved;
        private readonly RenderTexture _secondResolved;
        private int _currentIndex;
        private int _nextIndex = -1;
        private float _shotEndTime;
        private float _transitionStartTime;

        // Preserves camera state and allocates reusable render targets.
        internal CameraSequenceCompositor(CameraSequenceSettings settings, int width, int height, int antiAliasingSamples, float startTime)
        {
            _settings = settings;
            _random = settings.RandomSeed.HasValue ? new System.Random(settings.RandomSeed.Value) : new System.Random();
            foreach (Camera camera in settings.Cameras)
            {
                _cameraStates.Add(new CameraState(camera));
                camera.enabled = false;
            }
            _currentIndex = settings.Order == CameraSequenceOrder.Random ? _random.Next(settings.Cameras.Count) : 0;
            _firstTarget = CreateTarget(width, height, antiAliasingSamples);
            _secondTarget = CreateTarget(width, height, antiAliasingSamples);
            _firstResolved = CreateTarget(width, height, 1);
            _secondResolved = CreateTarget(width, height, 1);
            _shotEndTime = startTime + NextShotDuration();
        }

        // Renders the active camera and blends the next camera during a transition.
        internal void Render(RenderTexture output, float time)
        {
            if (_nextIndex < 0 && time >= _shotEndTime)
            {
                _nextIndex = SelectNextIndex();
                _transitionStartTime = time;
            }
            RenderCamera(_settings.Cameras[_currentIndex], _firstTarget, _firstResolved);
            Graphics.Blit(_firstResolved, output);
            if (_nextIndex < 0)
            {
                return;
            }
            float duration = Math.Max(0.001f, _settings.CrossFadeDurationSeconds);
            float opacity = Mathf.Clamp01((time - _transitionStartTime) / duration);
            RenderCamera(_settings.Cameras[_nextIndex], _secondTarget, _secondResolved);
            DrawOverlay(output, _secondResolved, opacity);
            if (opacity >= 1f)
            {
                _currentIndex = _nextIndex;
                _nextIndex = -1;
                _shotEndTime = time + NextShotDuration();
            }
        }

        // Restores every source camera and releases owned render textures.
        public void Dispose()
        {
            foreach (CameraState state in _cameraStates)
            {
                state.Restore();
            }
            Release(_firstTarget);
            Release(_secondTarget);
            Release(_firstResolved);
            Release(_secondResolved);
        }

        // Renders one source camera and resolves MSAA into a sampleable texture.
        private static void RenderCamera(Camera camera, RenderTexture target, RenderTexture resolved)
        {
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previous;
            Graphics.Blit(target, resolved);
        }

        // Alpha-blends the incoming camera over the current output.
        private static void DrawOverlay(RenderTexture output, Texture incoming, float opacity)
        {
            RenderTexture previous = RenderTexture.active;
            Graphics.SetRenderTarget(output);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, output.width, output.height, 0);
            Graphics.DrawTexture(new Rect(0, 0, output.width, output.height), incoming,
                new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(1, 1, 1, opacity));
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        // Selects the next sequential or random camera without immediate repetition.
        private int SelectNextIndex()
        {
            if (_settings.Order == CameraSequenceOrder.Sequential)
            {
                return (_currentIndex + 1) % _settings.Cameras.Count;
            }
            int candidate = _random.Next(_settings.Cameras.Count - 1);
            return candidate >= _currentIndex ? candidate + 1 : candidate;
        }

        // Draws the next full-shot duration from the configured inclusive range.
        private float NextShotDuration()
        {
            float range = _settings.MaximumShotDurationSeconds - _settings.MinimumShotDurationSeconds;
            return _settings.MinimumShotDurationSeconds + (float)_random.NextDouble() * range;
        }

        // Creates one temporary sRGB camera target.
        private static RenderTexture CreateTarget(int width, int height, int antiAliasingSamples)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = antiAliasingSamples
            };
            target.Create();
            return target;
        }

        // Releases one temporary render texture.
        private static void Release(RenderTexture target)
        {
            target.Release();
            UnityEngine.Object.Destroy(target);
        }

        // Preserves the mutable fields changed for manual camera rendering.
        private readonly struct CameraState
        {
            private readonly Camera _camera;
            private readonly bool _enabled;
            private readonly RenderTexture _target;

            // Captures the original state of one source camera.
            internal CameraState(Camera camera)
            {
                _camera = camera;
                _enabled = camera.enabled;
                _target = camera.targetTexture;
            }

            // Restores the original state of one source camera.
            internal void Restore()
            {
                _camera.enabled = _enabled;
                _camera.targetTexture = _target;
            }
        }
    }
}
