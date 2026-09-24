# UnityRuntimeCameraRecorder

Record Unity cameras, render textures or the completed player screen with Unity audio. Each recorder creates one MP4 and can either capture one source or switch between several sources with cuts and crossfades.

**Start here:** follow the [Developer Getting Started guide](DEVELOPER_GUIDE.md) for step-by-step integration and focused code examples covering every public feature.

## Requirements

- Unity 6 with .NET Standard 2.1
- Windows x64 and Direct3D 11
- NVIDIA GPU with NVENC and a recent driver
- [FFmpeg](https://ffmpeg.org/download.html), installed separately

Import the complete recorder package into the Unity project. It includes all runtime DLLs and the crossfade shader; applications do not supply or maintain these files separately.

Build the Unity-ready archive with:

```bash
dotnet msbuild UnityRuntimeCameraRecorder.csproj -t:Package -p:Configuration=Release -p:UnityManagedPath="path/to/Unity/Editor/Data/Managed/UnityEngine"
```

## Minimal example

The application owns the camera, its target texture, resolution, MSAA and effects.

```csharp
using FFmpegMediaWriter;
using UnityRuntimeCameraRecorder;

var recorder = gameObject.AddComponent<global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder>();
var sequence = new VideoSequenceSettings
{
    Sources = new[] { VideoSequenceSource.FromCamera(camera) }
};

recorder.RecordingCompleted += () => Debug.Log("MP4 ready");
recorder.RecordingFailed += Debug.LogException;

recorder.StartRecording(sequence, audioListener, new RecordingSettings
{
    FfmpegPath = @"C:\tools\ffmpeg\bin\",
    TemporaryContainerPath = @"C:\Captures\capture.mkv.tmp",
    OutputPath = @"C:\Captures\capture.mp4",
    Width = 3840,
    Height = 2160,
    MaximumFrameRate = 60,
    QualityPreset = RecordingQualityPreset.Highest,
    VideoStreamFormat = VideoStreamFormat.H264
});

// Later. Finalization continues asynchronously.
recorder.StopRecording();
```

Camera sources must remain enabled and have a valid `targetTexture`. Keep all sources alive until `RecordingCompleted` or `RecordingFailed` is raised.

## Main features

- Camera, `Texture` and screen sources with optional cursor capture
- One source per MP4 or several sources edited into one MP4
- Sequential or shuffled source order
- GPU crossfades and immediate cuts
- Concurrent independent recorder instances
- H.264 and HEVC through NVIDIA NVENC
- Unity audio encoded as stereo AAC
- Optional asynchronous statistics generation
- PNG or JPEG image-sequence capture without FFmpeg or NVENC
- Explicit lifecycle events, diagnostics and logging callbacks

## Quality profiles

| Profile | NVENC | Video CQP | AAC audio |
| --- | ---: | ---: | ---: |
| Low | P5 | 32 | 96 kbit/s |
| Medium | P5 | 27 | 128 kbit/s |
| High | P5 | 23 | 192 kbit/s |
| Highest (default) | P5 | 16 | 192 kbit/s |

H.264 is the default codec. HDR is not supported. The configured FPS is a ceiling, not a guarantee.

## Build

Use the .NET 10 SDK and supply Unity's managed assembly directory:

```bash
dotnet build UnityRuntimeCameraRecorder.csproj -c Release \
  -p:UnityManagedPath="YOUR_UNITY_MANAGED_DIRECTORY"
```

The output is `bin/Release/netstandard2.1/UnityRuntimeCameraRecorder.dll`. Keep the recorder, [FFmpegMediaWriter](https://github.com/CineCapture/FFmpegMediaWriter) and [Direct3DVideoEncoder](https://github.com/CineCapture/Direct3DVideoEncoder) repositories side by side when building from source.

See [UnitySample](https://github.com/CineCapture/UnitySample) for an editable scene. The project uses the [MIT license](LICENSE); third-party licenses and codec patent rights are separate.
