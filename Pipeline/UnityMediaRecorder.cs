using System;
using System.IO;
using System.Threading.Tasks;
using FFmpegMediaWriter;
using UnityEngine;

namespace UnityMediaRecorder
{
    // Coordinates Unity capture, native encoding, FFmpeg multiplexing and MP4 finalization.
    public sealed class UnityMediaRecorder : MonoBehaviour
    {
        private IMediaWriter _writer;
        private UnityAudioCapture _audio;
        private VideoCaptureBackend _videoBackend;
        private Camera _camera;
        private AudioListener _listener;
        private RecordingSettings _settings;
        private RecordingQualityProfile _qualityProfile;
        private RenderTexture _preparedVideoTarget;
        private bool _waitingForAudio;
        private bool _waitingForPipes;
        private bool _videoCaptureStarted;
        private VideoStreamFormat _videoStreamFormat;
        private bool _writerStarted;
        private PngSequenceCapture _pngSequenceCapture;
        private int _lastCapturedPngFrameCount;
        private float _captureStartTime;
        private float _captureDuration;
        private Task _statisticsTask;
        public event Action CaptureStarted;
        public event Action CaptureStarting;
        public event Action FinalizationStarted;
        public event Action RecordingCompleted;
        public event Action<Exception> RecordingFailed;
        public bool IsCapturing => _writerStarted && !IsFinalizing || _waitingForAudio || _waitingForPipes || _pngSequenceCapture != null;
        public bool IsFinalizing => _writer?.IsFinalizing == true;
        public bool IsBusy => IsCapturing || IsFinalizing || _statisticsTask != null;
        public string ActiveVideoBackendName => _videoBackend?.Name;
        // Retains optional backend telemetry after capture resources have been released.
        public string LastVideoDiagnosticsJson { get; private set; }
        public int CapturedPngFrameCount => _pngSequenceCapture?.CapturedFrameCount ?? _lastCapturedPngFrameCount;

        // Creates capture resources and begins one asynchronous recording session.
        public void StartRecording(Camera camera, AudioListener listener, RecordingSettings settings, RenderTexture preparedVideoTarget = null)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("The media recorder is already busy.");
            }

            ValidateArguments(camera, listener, settings);
            if (settings.CaptureHdr) throw new NotSupportedException("HDR recording is not supported by the SDR quality profile.");
            LastVideoDiagnosticsJson = null;
            _camera = camera;
            _listener = listener;
            _qualityProfile = RecordingQualityProfile.FromPreset(settings.QualityPreset, settings.Width, settings.Height, settings.MaximumFrameRate);
            _settings = settings;
            _preparedVideoTarget = preparedVideoTarget;
            try
            {
                _videoBackend = VideoCaptureBackendRegistry.Create(gameObject);
                _videoBackend.ConfigureStreamFormat(settings.VideoStreamFormat);
                _videoStreamFormat = _videoBackend.StreamFormat;
                MediaRecorderLog.WriteInfo($"Selected video backend: {_videoBackend.Name}.");
                _writer = new FFmpegMediaWriter.FfmpegMediaWriter();
                _audio = _listener.gameObject.AddComponent<UnityAudioCapture>();
                _audio.Initialize(data => _writer?.WriteAudio(data) == true);
                _waitingForAudio = true;
            }
            catch
            {
                ReleaseCaptureProducers();
                ReleaseWriter();
                throw;
            }
        }

        // Starts a camera-only PNG image sequence without FFmpeg, audio or a video encoder.
        public void StartPngSequence(Camera camera, PngSequenceSettings settings, RenderTexture preparedTarget = null)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("The media recorder is already busy.");
            }

            ValidatePngSequenceArguments(camera, settings);
            try
            {
                _lastCapturedPngFrameCount = 0;
                _pngSequenceCapture = gameObject.AddComponent<PngSequenceCapture>();
                CaptureStarting?.Invoke();
                _pngSequenceCapture.StartCapture(camera, settings, preparedTarget);
                CaptureStarted?.Invoke();
            }
            catch
            {
                ReleasePngSequenceCapture();
                throw;
            }
        }

        // Stops the active PNG sequence and reports it as completed immediately.
        public void StopPngSequence()
        {
            if (_pngSequenceCapture == null)
            {
                return;
            }

            ReleasePngSequenceCapture();
            RecordingCompleted?.Invoke();
        }

        // Stops active capture and starts creation of the final MP4 file.
        public void StopRecording()
        {
            _waitingForAudio = false;
            _waitingForPipes = false;
            ReleaseCaptureProducers();
            if (_writerStarted)
            {
                StartFinalization();
            }
            else
            {
                ReleaseWriter();
            }
        }

        // Advances audio initialization, pipe connection and background finalization.
        private void Update()
        {
            if (_waitingForAudio && _audio.IsReady)
            {
                StartFfmpeg();
            }
            else if (_waitingForPipes)
            {
                AdvancePipeStartup();
            }

            if (_writer?.IsFinalizationCompleted == true)
            {
                CompleteFinalization();
            }
            if (_statisticsTask?.IsCompleted == true)
            {
                CompleteStatistics();
            }
        }

        // Releases active processes and capture resources when the component is destroyed.
        private void OnDestroy()
        {
            _waitingForAudio = false;
            _waitingForPipes = false;
            ReleaseCaptureProducers();
            ReleasePngSequenceCapture();
            _writer?.Abort();
            ReleaseWriter();
        }

        // Stops and destroys the current PNG sequence capture component.
        private void ReleasePngSequenceCapture()
        {
            if (_pngSequenceCapture == null)
            {
                return;
            }

            _lastCapturedPngFrameCount = _pngSequenceCapture.CapturedFrameCount;
            _pngSequenceCapture.StopCapture();
            Destroy(_pngSequenceCapture);
            _pngSequenceCapture = null;
        }

        // Starts FFmpeg after Unity has reported the audio stream format.
        private void StartFfmpeg()
        {
            _waitingForAudio = false;
            try
            {
                _writer.Start(new MediaWriterSettings { FfmpegPath = _settings.FfmpegPath, TemporaryContainerPath = _settings.TemporaryContainerPath, ArchivePath = _settings.ArchivePath, KeepIntermediateFile = _settings.KeepIntermediateFile, OutputPath = _settings.OutputPath, MaximumFrameRate = _settings.MaximumFrameRate, AudioSampleRate = _audio.SampleRate, AudioChannels = _audio.Channels, VideoStreamFormat = _videoStreamFormat, EncodedVideoHasPresentationTimestamps = true, AudioCodec = AudioEncodingCodec.Aac, AudioBitRate = _qualityProfile.AudioBitRate, OutputAudioSampleRate = _qualityProfile.AudioSampleRate, OutputAudioChannels = _qualityProfile.AudioChannels, Warning = MediaRecorderLog.WriteWarning, Error = MediaRecorderLog.WriteError });
                _writerStarted = true;
                _waitingForPipes = true;
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        // Starts video capture and reports readiness after both FFmpeg pipes connect.
        private void AdvancePipeStartup()
        {
            if (_writer.IsVideoInputConnected && !_videoCaptureStarted)
            {
                try
                {
                    SavePreviewImage();
                    CaptureStarting?.Invoke();
                    StartVideoCapture();
                }
                catch (Exception exception)
                {
                    Fail(exception);
                    return;
                }
            }

            if (_writer.AreInputsConnected)
            {
                _waitingForPipes = false;
                CaptureStarted?.Invoke();
            }
        }

        // Starts the selected backend that produces encoded video packets.
        private void StartVideoCapture()
        {
            bool flipVertically = _settings.FlipVertically ?? (!_settings.CaptureScreen && SystemInfo.graphicsUVStartsAtTop);
            var context = new VideoCaptureContext(_camera, _settings.Width, _settings.Height, _settings.MaximumFrameRate, _settings.AntiAliasingSamples, _qualityProfile, _settings.OptimizeForConcurrentEncoding, flipVertically, _settings.CaptureScreen, _preparedVideoTarget, _writer.WriteVideoPacket);
            _videoBackend.StartCapture(context);
            _captureStartTime = Time.realtimeSinceStartup;
            _videoCaptureStarted = true;
        }

        // Saves the prepared camera frame when preview-image generation is enabled for the session.
        private void SavePreviewImage()
        {
            if (!_settings.GeneratePreviewImage)
            {
                return;
            }

            RenderTexture source = _preparedVideoTarget ?? _camera.targetTexture;
            if (source == null)
            {
                throw new InvalidOperationException("Preview-image generation requires a prepared video target or a camera target texture.");
            }

            RenderTexture readableSource = source;
            RenderTexture resolvedSource = null;
            if (source.antiAliasing > 1)
            {
                resolvedSource = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                resolvedSource.Create();
                Graphics.Blit(source, resolvedSource);
                readableSource = resolvedSource;
            }

            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(readableSource.width, readableSource.height, TextureFormat.RGBA32, false, false);
            try
            {
                RenderTexture.active = readableSource;
                image.ReadPixels(new Rect(0f, 0f, readableSource.width, readableSource.height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(_settings.PreviewImagePath, image.EncodeToPNG());
                MediaRecorderLog.WriteInfo($"Recording preview saved: {_settings.PreviewImagePath}");
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(image);
                if (resolvedSource != null)
                {
                    resolvedSource.Release();
                    Destroy(resolvedSource);
                }
            }
        }

        // Stops Unity capture components while leaving writer shutdown to the caller.
        private void ReleaseCaptureProducers()
        {
            if (_videoBackend != null)
            {
                if (_videoCaptureStarted)
                {
                    _captureDuration = Mathf.Max(0, Time.realtimeSinceStartup - _captureStartTime);
                    _videoBackend.StopCapture();
                    LastVideoDiagnosticsJson = _videoBackend.DiagnosticsJson;
                }

                Destroy(_videoBackend);
                _videoBackend = null;
            }

            _videoCaptureStarted = false;
            if (_audio != null)
            {
                Destroy(_audio);
                _audio = null;
            }
        }

        // Starts background MP4 creation without re-encoding native H.265 video.
        private void StartFinalization()
        {
            try
            {
                _writer.FinishCapture();
                FinalizationStarted?.Invoke();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        // Validates the completed MP4 and applies the requested intermediate-file policy.
        private void CompleteFinalization()
        {
            try
            {
                _writer.CompleteFinalization();
                ReleaseWriter();
                if (_settings.GenerateStatistics)
                {
                    StartStatistics();
                }
                else
                {
                    RecordingCompleted?.Invoke();
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        // Starts optional per-video statistics generation on a worker thread.
        private void StartStatistics()
        {
            string ffprobePath = _settings.FfprobePath;
            if (string.IsNullOrWhiteSpace(ffprobePath))
            {
                ffprobePath = Path.Combine(Path.GetDirectoryName(_settings.FfmpegPath) ?? string.Empty, "ffprobe.exe");
            }
            string statisticsPath = string.IsNullOrWhiteSpace(_settings.StatisticsPath)
                ? Path.ChangeExtension(_settings.OutputPath, ".stats.txt") : _settings.StatisticsPath;
            var snapshot = new VideoStatisticsSnapshot(_settings, _qualityProfile, LastVideoDiagnosticsJson,
                _captureDuration, QualitySettings.vSyncCount, ffprobePath, statisticsPath);
            _statisticsTask = Task.Run(snapshot.Write);
        }

        // Reports completion after optional statistics generation has finished.
        private void CompleteStatistics()
        {
            Task task = _statisticsTask;
            _statisticsTask = null;
            if (task.IsFaulted)
            {
                MediaRecorderLog.WriteWarning("Cannot generate video statistics: " + task.Exception?.GetBaseException().Message);
            }
            RecordingCompleted?.Invoke();
        }

        // Reports a session failure after releasing active capture resources.
        private void Fail(Exception exception)
        {
            _waitingForAudio = false;
            _waitingForPipes = false;
            ReleaseCaptureProducers();
            _writer?.Abort();
            ReleaseWriter();
            MediaRecorderLog.WriteError(exception);
            RecordingFailed?.Invoke(exception);
        }

        // Disposes the current format-neutral media writer.
        private void ReleaseWriter()
        {
            _writer?.Dispose();
            _writer = null;
            _writerStarted = false;
        }

        // Rejects missing or invalid session arguments before resources are allocated.
        private static void ValidateArguments(Camera camera, AudioListener listener, RecordingSettings settings)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.FfmpegPath))
            {
                throw new ArgumentException("FfmpegPath must specify the external FFmpeg executable.", nameof(settings));
            }

            if (settings.Width < 2 || settings.Height < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            if (settings.MaximumFrameRate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            if (settings.AntiAliasingSamples != 1 && settings.AntiAliasingSamples != 2 && settings.AntiAliasingSamples != 4 && settings.AntiAliasingSamples != 8)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Anti-aliasing samples must be 1, 2, 4 or 8.");
            }

            if (string.IsNullOrWhiteSpace(settings.TemporaryContainerPath))
            {
                throw new ArgumentException("A temporary container path is required.", nameof(settings));
            }

            if (settings.KeepIntermediateFile && string.IsNullOrWhiteSpace(settings.ArchivePath))
            {
                throw new ArgumentException("An archive path is required when keeping the intermediate file.", nameof(settings));
            }

            if (settings.GeneratePreviewImage && string.IsNullOrWhiteSpace(settings.PreviewImagePath))
            {
                throw new ArgumentException("A preview image path is required when generating a preview image.", nameof(settings));
            }

            if (settings.GenerateStatistics && string.IsNullOrWhiteSpace(settings.FfmpegPath))
            {
                throw new ArgumentException("Statistics generation requires FFmpeg and FFprobe paths.", nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.OutputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(settings));
            }
        }

        // Rejects missing or invalid PNG sequence arguments before resources are allocated.
        private static void ValidatePngSequenceArguments(Camera camera, PngSequenceSettings settings)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
            {
                throw new ArgumentException("An output directory is required.", nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.FileNamePrefix))
            {
                throw new ArgumentException("A file name prefix is required.", nameof(settings));
            }

            if (settings.FileNamePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("The file name prefix contains invalid characters.", nameof(settings));
            }

            if (settings.Width < 2 || settings.Height < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            if (double.IsNaN(settings.CapturesPerSecond) || double.IsInfinity(settings.CapturesPerSecond) || settings.CapturesPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            if (double.IsNaN(settings.InitialDelaySeconds) || double.IsInfinity(settings.InitialDelaySeconds) || settings.InitialDelaySeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            if (settings.AntiAliasingSamples != 1 && settings.AntiAliasingSamples != 2 && settings.AntiAliasingSamples != 4 && settings.AntiAliasingSamples != 8)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Anti-aliasing samples must be 1, 2, 4 or 8.");
            }
        }
    }
}
