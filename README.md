# UnityRuntimeCameraRecorder

Record one or more explicit Unity video sources with audio. Each recorder creates one MP4. The application owns source rendering, resolution, anti-aliasing, effects and recording duration.

Audio is encoded as AAC at 128 or 192 kbit/s according to the quality profile, without applying gain or fades to the captured audio.

## Requirements

The DLL targets .NET Standard 2.1 for compatible Unity runtimes on Windows, Linux and macOS. Unity 6's referenced assemblies require 2.1. PNG capture uses Unity's GPU readback APIs and requires a graphics device supporting asynchronous readback. Linux/macOS execution has not yet been tested.

For the current video backend: Windows x64, Unity running Direct3D 11, an NVIDIA GPU with NVENC and a recent driver. Include `UnityRuntimeCameraRecorder.dll`, [Direct3DVideoEncoder.dll](https://github.com/UnityRuntimeCameraRecorder/Direct3DVideoEncoder) and [FFmpegMediaWriter.dll](https://github.com/UnityRuntimeCameraRecorder/FFmpegMediaWriter).

Install FFmpeg separately and supply its executable path in `RecordingSettings.FfmpegPath`.

Follow the [FFmpeg download and setup instructions](https://github.com/UnityRuntimeCameraRecorder/FFmpegMediaWriter#download-and-setup).

## Getting started

Follow the [UnityRuntimeCameraRecorder Getting Started guide](DEVELOPER_GUIDE.md) to integrate cameras, screen capture, textures, multi-source sequences, transitions, statistics and PNG capture step by step.

## Video example

Run this from your Unity component. The application creates and owns the camera target. Camera sources must be enabled and have a target texture. Create the output directory and use unused filenames. The FFmpeg path is only an example.

```csharp
using FFmpegMediaWriter;
using UnityEngine;
using UnityRuntimeCameraRecorder;

var recorder = gameObject.AddComponent<global::UnityRuntimeCameraRecorder.UnityRuntimeCameraRecorder>();
recorder.RecordingCompleted += () => Debug.Log("MP4 ready");
recorder.RecordingFailed += error => Debug.LogException(error);

camera.allowMSAA = true;
camera.targetTexture = new RenderTexture(3840, 2160, 24)
{
    antiAliasing = 4
};
camera.targetTexture.Create();

var sources = new VideoSequenceSettings
{
    Sources = new[] { VideoSequenceSource.FromCamera(camera) }
};

recorder.StartRecording(sources, listener, new RecordingSettings
{
    FfmpegPath = @"C:\tools\ffmpeg\bin\ffmpeg.exe",
    TemporaryContainerPath = @"C:\Captures\session.mkv.tmp",
    OutputPath = @"C:\Captures\session.mp4",
    Width = 3840,
    Height = 2160,
    MaximumFrameRate = 60,
    SourceAntiAliasingSamples = 4,
    QualityPreset = RecordingQualityPreset.High,
    VideoStreamFormat = VideoStreamFormat.Hevc
});

// Later: stop capture, then wait for completion or failure.
recorder.StopRecording();
```

Keep the recorder, sources and listener alive through finalization. Release application-owned render textures after finalization. For two videos, use two recorder components. The FPS setting is a ceiling, not a guarantee.

Options in `RecordingSettings`:

- `VideoStreamFormat`: H.264 (library default) or HEVC. NVENC completion is asynchronous by default.
- `SourceAntiAliasingSamples`: reports the application's source MSAA level in generated statistics. The recorder does not configure MSAA.
- `GeneratePreviewImage = true` with `PreviewImagePath`: save a PNG just before video capture.
- `KeepIntermediateFile = true` with `ArchivePath`: keep the MKV after successful MP4 creation.

FFmpeg assembles encoded video and audio without recompressing video. Audio comes from Unity's mix.

## Multiple sources in one video

Use the same explicit source list for one or more inputs. Multiple sources switch within one MP4 and one NVENC session. Sequential order follows the list; random order visits every source before starting another cycle. Each shot duration is drawn independently from the configured range.

```csharp
var sequence = new VideoSequenceSettings
{
    Sources = new[]
    {
        VideoSequenceSource.FromCamera(camera1),
        VideoSequenceSource.FromCamera(camera2),
        VideoSequenceSource.FromTexture(gameRenderTexture),
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
    },
    RandomSeed = 42 // Optional: reproduce the same edit.
};

recorder.StartRecording(sequence, listener, recordingSettings);
```

The crossfade duration defaults to half a second. `NoTransition` performs an immediate cut. When several transition types are allowed, one is selected for each source change. A screen source adds the completed player frame, UI and cursor. A texture source reads its current GPU content without an extra encoder. Camera sources must be enabled and have a target texture. The recorder reads their completed GPU frames without changing their live rendering, previews or temporal effects. Invalid camera sources fail when capture starts. During a crossfade, the outgoing and incoming sources are blended on the GPU with complementary opacity.

Copy `Resources/UnityRuntimeCameraRecorderCrossFade.shader` into a Unity `Assets/Resources` folder when installing the recorder DLL manually. Unity packages should include this shader asset with the runtime assembly.

## PNG sequence instead of video

This mode uses no FFmpeg, NVENC or audio.

```csharp
recorder.StartPngSequence(camera, new PngSequenceSettings
{
    OutputDirectory = @"C:\Captures\Sequence",
    FileNamePrefix = "frame_",
    Width = 1920,
    Height = 1080,
    CapturesPerSecond = 1,
    AntiAliasingSamples = 4
});

// Later.
recorder.StopPngSequence();
```

## Build and extension

Build with the .NET 10 SDK: `dotnet build UnityRuntimeCameraRecorder.csproj -c Release -p:UnityManagedPath="YOUR_UNITY_MANAGED_DIRECTORY"`. Set `UnityManagedPath` to Unity's managed UnityEngine assembly directory. Output: `bin/Release/netstandard2.1/UnityRuntimeCameraRecorder.dll`. Keep the three library repositories side by side for the project reference and native DLL copy. The current Direct3D/NVENC video backend remains Windows-only; Linux/macOS video recording requires a separately registered compatible backend.

To add a video engine, implement `VideoCaptureBackend`, validate codec selection in `ConfigureStreamFormat` and register a factory with `VideoCaptureBackendRegistry.Register`. Write packets through `VideoCaptureContext.WritePacket`, not directly to FFmpeg. No software encoding fallback is included.

See [UnitySample](https://github.com/UnityRuntimeCameraRecorder/UnitySample) for an editable scene. Our code uses the [MIT license](LICENSE); third-party licenses and codec patent rights are separate.

## Automatic SDR quality profiles

`RecordingSettings.QualityPreset` accepts only `Low`, `Medium` or `High` (default). Video and audio settings are derived internally; no manual NVENC preset is exposed.

| Profile | NVENC preset | Base video QP (CQP) | Audio |
|---|---:|---:|---|
| Low | P5 | 27 | AAC 128 kbit/s |
| Medium | P5 | 23 | AAC 192 kbit/s |
| High (default) | P5 | 16 | AAC 192 kbit/s |

H.264 is the default. Audio is 48000 Hz stereo. For any supported positive even resolution, effective QP is `clamp(baseQP - floor((1 - min(2000, sqrt(width*width + height*height))/2000)*10), 1, 51)`. FPS remains independent; CQP has no target video bitrate.

**HDR is not supported today:** `CaptureHdr = true` is rejected. HEVC SDR is locally checked on RTX 5060 for all 3 qualities at 4K/60 FPS, including a 120 s High capture under load; other HEVC configurations remain to be validated. See [Direct3DVideoEncoder NVENC settings](https://github.com/UnityRuntimeCameraRecorder/Direct3DVideoEncoder#sdr-constant-qp-quality-entry-point) for GPU capability handling and effective native parameters. Matching managed/native DLLs are required.

Sources: [OBS quality mapping](https://github.com/obsproject/obs-studio/blob/master/frontend/utility/SimpleOutput.cpp) for Medium/High QP and resolution correction; [NVIDIA NVENC guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/nvenc-video-encoder-api-prog-guide/index.html) for encoder controls. [NVIDIA App documentation](https://nvidia.custhelp.com/app/answers/detail/a_id/5713) describes its VBR behavior, not our CQP profiles. Low QP 27 and the audio mapping are project choices, not externally specified presets.
