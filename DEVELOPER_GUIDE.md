# Getting Started

This step-by-step guide introduces every public feature of `UnityRuntimeCameraRecorder`. It only shows the code that matters for each step.

Each recorder creates one MP4. That MP4 can contain one source or switch between several camera, texture and screen sources.

## 1. Install the recorder package

Import the complete recorder package into the Unity project. It includes all runtime DLLs and the crossfade shader; the application does not maintain these files separately.

Install FFmpeg separately. The current video backend requires Windows x64, Direct3D 11 and an NVIDIA GPU with NVENC.

Add the required namespaces to the recording component:

```csharp
using System;
using System.IO;
using FFmpegMediaWriter;
using UnityEngine;
using UnityRuntimeCameraRecorder;
```

## 2. Create a recorder

Add one recorder component for each MP4 that the application will create:

```csharp
private global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder recorder;

void Awake()
{
    recorder = gameObject.AddComponent<global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder>();
}
```

The fully qualified type name avoids ambiguity between the namespace and class, which share the same name.

## 3. Prepare a camera source

The application owns camera rendering. A recorded camera must remain enabled and render into a valid `RenderTexture`.

```csharp
RenderTexture cameraTarget = new RenderTexture(3840, 2160, 24)
{
    antiAliasing = 4,
    name = "Camera Recording Target"
};
cameraTarget.Create();

camera.allowMSAA = true;
camera.targetTexture = cameraTarget;
```

The application also owns resolution, MSAA, post-processing and other camera effects. The recorder reads the completed texture without changing them.

## 4. Configure the output

Create the destination directory and configure one recording session:

```csharp
string directory = Path.Combine(Application.persistentDataPath, "Recordings");
Directory.CreateDirectory(directory);

var settings = new RecordingSettings
{
    FfmpegPath = @"C:\tools\ffmpeg\bin\",
    TemporaryContainerPath = Path.Combine(directory, "capture.mkv.tmp"),
    OutputPath = Path.Combine(directory, "capture.mp4"),
    Width = 3840,
    Height = 2160,
    MaximumFrameRate = 60,
    SourceAntiAliasingSamples = 4,
    QualityPreset = RecordingQualityPreset.Highest,
    VideoStreamFormat = VideoStreamFormat.H264
};
```

Use unused temporary and output filenames. Width and height must be positive even values. `MaximumFrameRate` is a ceiling, not a guarantee.

`SourceAntiAliasingSamples` is statistics metadata. It does not enable MSAA.

## 5. Record the first camera

Create a sequence containing the camera and pass the scene's `AudioListener`:

```csharp
var sequence = new VideoSequenceSettings
{
    Sources = new[] { VideoSequenceSource.FromCamera(camera) }
};

recorder.StartRecording(sequence, audioListener, settings);
```

Unity's final audio mix is recorded as stereo AAC. Keep the recorder, listener, camera and texture alive until the operation completes.

## 6. Stop and wait for completion

Stopping capture starts asynchronous MP4 finalization:

```csharp
recorder.RecordingCompleted += () => Debug.Log("MP4 is ready");
recorder.RecordingFailed += exception => Debug.LogException(exception);

// Call later.
recorder.StopRecording();
```

Useful lifecycle events and state properties are:

```csharp
recorder.CaptureStarting += () => Debug.Log("Preparing capture");
recorder.CaptureStarted += () => Debug.Log("Capture started");
recorder.FinalizationStarted += () => Debug.Log("Finalizing MP4");

bool capturing = recorder.IsCapturing;
bool finalizing = recorder.IsFinalizing;
bool busy = recorder.IsBusy;
```

Do not start another operation while `IsBusy` is true. `RecordingCompleted` is raised after optional statistics generation too.

## 7. Choose another source type

Record the completed player frame, including UI and the cursor by default:

```csharp
sequence.Sources = new[] { VideoSequenceSource.FromScreen() };
```

Exclude the cursor when required:

```csharp
sequence.Sources = new[] { VideoSequenceSource.FromScreen(captureCursor: false) };
```

Record a texture produced by another rendering system:

```csharp
sequence.Sources = new[] { VideoSequenceSource.FromTexture(gameRenderTexture) };
```

The producer must keep updating the texture during capture.

## 8. Combine several sources into one MP4

Pass several sources to create one edited video with one encoder session:

```csharp
var sequence = new VideoSequenceSettings
{
    Sources = new[]
    {
        VideoSequenceSource.FromCamera(camera1),
        VideoSequenceSource.FromCamera(camera2),
        VideoSequenceSource.FromScreen()
    },
    Order = VideoSequenceOrder.Random,
    MinimumShotDurationSeconds = 4f,
    MaximumShotDurationSeconds = 8f,
    CrossFadeDurationSeconds = 0.5f,
    Transitions = new[]
    {
        VideoSequenceTransition.CrossFade,
        VideoSequenceTransition.NoTransition
    }
};
```

Random order visits every source before starting another cycle. Each shot receives a duration from the configured range. One allowed transition is selected at every source change.

Use a fixed order and hard cuts when needed:

```csharp
sequence.Order = VideoSequenceOrder.Sequential;
sequence.Transitions = new[] { VideoSequenceTransition.NoTransition };
```

Make random choices reproducible for tests or replays:

```csharp
sequence.RandomSeed = 12345;
```

## 9. Create several MP4 files concurrently

Use a separate recorder, sequence and path set for every output:

```csharp
var camera1Recorder = gameObject.AddComponent<global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder>();
var camera2Recorder = gameObject.AddComponent<global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder>();

camera1Settings.OptimizeForConcurrentEncoding = true;
camera2Settings.OptimizeForConcurrentEncoding = true;

camera1Recorder.StartRecording(camera1Sequence, audioListener, camera1Settings);
camera2Recorder.StartRecording(camera2Sequence, audioListener, camera2Settings);
```

Each recorder captures, stops and finalizes independently. Never reuse its temporary or output paths for another recorder.

## 10. Select quality and codec

```csharp
settings.QualityPreset = RecordingQualityPreset.Low; // Low, Medium, High or Highest
settings.VideoStreamFormat = VideoStreamFormat.Hevc; // H264 or Hevc
```

H.264 and `Highest` are the defaults. HEVC requires support from the active backend and GPU. Quality profiles configure video CQP, NVENC settings and AAC bitrate automatically.

HDR recording is not currently supported. Setting `CaptureHdr = true` throws an error.

Override automatic vertical-orientation detection only when integration requires it:

```csharp
settings.FlipVertically = true;
```

## 11. Generate optional files

Generate a text statistics file after finalization:

```csharp
settings.GenerateStatistics = true;
settings.FfprobePath = @"C:\tools\ffmpeg\bin\ffprobe.exe";
settings.StatisticsPath = Path.Combine(directory, "capture.stats.txt");
```

Statistics run on a worker thread. When `StatisticsPath` is empty, the MP4 path is changed to `.stats.txt`. When `FfprobePath` is empty, the recorder looks for `ffprobe.exe` next to FFmpeg.

## 12. Read backend diagnostics

```csharp
string backend = recorder.ActiveVideoBackendName;

recorder.RecordingCompleted += () =>
{
    string diagnosticsJson = recorder.LastVideoDiagnosticsJson;
};
```

The last diagnostic JSON remains available after capture resources are released.

## 13. Capture an image sequence instead of video

Image sequence capture does not use FFmpeg, NVENC or audio:

```csharp
recorder.StartImageSequence(camera, new ImageSequenceSettings
{
    OutputDirectory = Path.Combine(directory, "Frames"),
    FileNamePrefix = "frame_",
    Width = 1920,
    Height = 1080,
    CapturesPerSecond = 2,
    InitialDelaySeconds = 1,
    AntiAliasingSamples = 4,
    EncoderThreadCount = 4,
    MaximumQueuedFrames = 16,
    MaximumFrameCount = 0,
    FileFormat = ImageSequenceFormat.Jpeg,
    JpegQuality = 95,
    FlipVertically = false
});
```

Increase `EncoderThreadCount` and `MaximumQueuedFrames` for short, high-rate exports. Queued 4K RGBA frames use about 32 MB each.

Stop it and inspect the resulting frame count:

```csharp
recorder.StopImageSequence();
Debug.Log(recorder.CapturedImageFrameCount);
```

An application-prepared `RenderTexture` can be supplied as the third argument to `StartImageSequence`.

## 14. Release application-owned resources

Release camera targets after recording completes or fails:

```csharp
void ReleaseCameraTarget()
{
    camera.targetTexture = null;
    cameraTarget.Release();
    Destroy(cameraTarget);
}

recorder.RecordingCompleted += ReleaseCameraTarget;
recorder.RecordingFailed += _ => ReleaseCameraTarget();
```

The recorder does not destroy resources created by the application.

## 15. Handle startup errors

Invalid arguments throw immediately. Runtime failures use `RecordingFailed`:

```csharp
try
{
    recorder.StartRecording(sequence, audioListener, settings);
}
catch (Exception exception)
{
    Debug.LogError($"Recording could not start: {exception.Message}");
}
```

Common causes are a disabled camera, a missing camera target, invalid dimensions, reused paths or missing executables.

## 16. Connect library logging

```csharp
RecorderLog.Info = message => Debug.Log($"[Recorder] {message}");
RecorderLog.Warning = message => Debug.LogWarning($"[Recorder] {message}");
RecorderLog.Error = exception => Debug.LogException(exception);
```

See the repository [README](README.md) for supported platforms, quality-profile details and backend extension points. See [UnitySample](https://github.com/CineCapture/UnitySample) for a complete editable scene.
