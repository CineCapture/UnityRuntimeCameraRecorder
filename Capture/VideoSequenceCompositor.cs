using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityRuntimeCameraRecorder
{
    // Renders a timed camera sequence into one GPU texture with crossfade transitions.
    internal sealed class VideoSequenceCompositor : IDisposable
    {
        private readonly VideoSequenceSettings _settings;
        private readonly IReadOnlyList<VideoSequenceSource> _sources;
        private readonly System.Random _random;
        private readonly List<int> _randomOrder = new List<int>();
        private readonly RenderTexture _firstResolved;
        private readonly RenderTexture _secondResolved;
        private readonly Material _crossFadeMaterial;
        private readonly bool _preFlipScreen;
        private int _currentIndex;
        private int _nextIndex = -1;
        private float _shotEndTime;
        private float _transitionStartTime;
        private VideoSequenceTransition _activeTransition;

        // Resolves source descriptors and allocates reusable render targets.
        internal VideoSequenceCompositor(VideoSequenceSettings settings, int width, int height, bool flipVertically, float startTime)
        {
            _settings = settings;
            _sources = settings.Sources;
            RequiresScreen = ContainsScreenSource(_sources);
            ValidateCameraSources();
            _preFlipScreen = flipVertically;
            _random = settings.RandomSeed.HasValue ? new System.Random(settings.RandomSeed.Value) : new System.Random();
            if (SourceCount > 1 && ContainsCrossFade(settings.Transitions))
            {
                Shader crossFadeShader = Shader.Find("UnityRuntimeCameraRecorder/CrossFade");
                if (crossFadeShader == null)
                {
                    throw new InvalidOperationException("The UnityRuntimeCameraRecorder/CrossFade shader is missing from the player build.");
                }

                _crossFadeMaterial = new Material(crossFadeShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            _currentIndex = settings.Order == VideoSequenceOrder.Random ? _random.Next(SourceCount) : 0;
            _firstResolved = CreateTarget(width, height, 1);
            _secondResolved = CreateTarget(width, height, 1);
            _shotEndTime = startTime + NextShotDuration();
        }

        // Renders the active camera and blends the next camera during a transition.
        internal void Render(RenderTexture output, RenderTexture screen, float time)
        {
            if (SourceCount > 1 && _nextIndex < 0 && time >= _shotEndTime)
            {
                _nextIndex = SelectNextIndex();
                _activeTransition = SelectTransition();
                _transitionStartTime = time;
                if (_activeTransition == VideoSequenceTransition.NoTransition)
                {
                    CompleteTransition(time);
                }
            }
            RenderSource(_currentIndex, screen, _firstResolved);
            Graphics.Blit(_firstResolved, output);
            if (_nextIndex < 0)
            {
                return;
            }
            float duration = Math.Max(0.001f, _settings.CrossFadeDurationSeconds);
            float opacity = Mathf.Clamp01((time - _transitionStartTime) / duration);
            RenderSource(_nextIndex, screen, _secondResolved);
            if (opacity >= 1f)
            {
                Graphics.Blit(_secondResolved, output);
                CompleteTransition(time);
                return;
            }

            DrawCrossFade(output, _firstResolved, _secondResolved, opacity);
        }

        internal bool RequiresScreen { get; private set; }
        private int SourceCount => _sources.Count;

        // Releases the compositor's materials and render textures.
        public void Dispose()
        {
            Release(_firstResolved);
            Release(_secondResolved);
            UnityEngine.Object.Destroy(_crossFadeMaterial);
        }

        // Renders either a Unity camera or the captured application screen.
        private void RenderSource(int index, RenderTexture screen, RenderTexture resolved)
        {
            VideoSequenceSource source = _sources[index];
            if (source.Kind == VideoSequenceSource.SourceKind.Camera)
            {
                Graphics.Blit(source.Camera.targetTexture, resolved);
                return;
            }
            if (source.Kind == VideoSequenceSource.SourceKind.Texture)
            {
                Graphics.Blit(source.Texture, resolved);
                return;
            }
            if (screen == null)
            {
                throw new InvalidOperationException("The camera sequence screen source is unavailable.");
            }
            if (_preFlipScreen)
            {
                Graphics.Blit(screen, resolved, new Vector2(1f, -1f), new Vector2(0f, 1f));
            }
            else
            {
                Graphics.Blit(screen, resolved);
            }
        }

        // Rejects camera sources that cannot provide a completed live GPU frame.
        private void ValidateCameraSources()
        {
            foreach (VideoSequenceSource source in _sources)
            {
                if (source.Kind != VideoSequenceSource.SourceKind.Camera)
                {
                    continue;
                }

                if (!source.Camera.enabled)
                {
                    throw new InvalidOperationException($"Sequence camera '{source.Camera.name}' must be enabled.");
                }

                if (source.Camera.targetTexture == null)
                {
                    throw new InvalidOperationException($"Sequence camera '{source.Camera.name}' must have a target texture.");
                }
            }
        }

        // Returns whether the sequence needs the completed player frame.
        private static bool ContainsScreenSource(IReadOnlyList<VideoSequenceSource> sources)
        {
            foreach (VideoSequenceSource source in sources)
            {
                if (source.Kind == VideoSequenceSource.SourceKind.Screen)
                {
                    return true;
                }
            }

            return false;
        }

        // Returns whether the configured transition pool can require the blend shader.
        private static bool ContainsCrossFade(IReadOnlyList<VideoSequenceTransition> transitions)
        {
            foreach (VideoSequenceTransition transition in transitions)
            {
                if (transition == VideoSequenceTransition.CrossFade)
                {
                    return true;
                }
            }

            return false;
        }

        // Blends two sources with complementary weights in one GPU pass.
        private void DrawCrossFade(RenderTexture output, Texture outgoing, Texture incoming, float opacity)
        {
            if (_crossFadeMaterial == null)
            {
                throw new InvalidOperationException("The crossfade material was not initialized.");
            }

            _crossFadeMaterial.SetTexture("_IncomingTex", incoming);
            _crossFadeMaterial.SetFloat("_Blend", opacity);
            Graphics.Blit(outgoing, output, _crossFadeMaterial);
        }

        // Selects the next sequential or random camera without immediate repetition.
        private int SelectNextIndex()
        {
            if (_settings.Order == VideoSequenceOrder.Sequential)
            {
                return (_currentIndex + 1) % SourceCount;
            }

            if (_randomOrder.Count == 0)
            {
                RefillRandomOrder();
            }

            int next = _randomOrder[0];
            _randomOrder.RemoveAt(0);
            return next;
        }

        // Selects one configured transition with the sequence random generator.
        private VideoSequenceTransition SelectTransition()
        {
            int index = _settings.Transitions.Count == 1 ? 0 : _random.Next(_settings.Transitions.Count);
            return _settings.Transitions[index];
        }

        // Promotes the incoming source and schedules its next cut or transition.
        private void CompleteTransition(float time)
        {
            _currentIndex = _nextIndex;
            _nextIndex = -1;
            _shotEndTime = time + NextShotDuration();
        }

        // Shuffles every source except the currently visible one into the next random cycle.
        private void RefillRandomOrder()
        {
            for (int index = 0; index < SourceCount; index++)
            {
                if (index != _currentIndex)
                {
                    _randomOrder.Add(index);
                }
            }

            for (int index = _randomOrder.Count - 1; index > 0; index--)
            {
                int swapIndex = _random.Next(index + 1);
                (_randomOrder[index], _randomOrder[swapIndex]) = (_randomOrder[swapIndex], _randomOrder[index]);
            }
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
            if (RenderTexture.active == target)
            {
                RenderTexture.active = null;
            }

            target.Release();
            UnityEngine.Object.Destroy(target);
        }

    }
}
