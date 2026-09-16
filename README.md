# UnityMediaRecorder

Record a Unity camera, or the application's displayed view, with audio. Each recorder creates one MP4. The caller controls the camera, buttons, paths and recording duration.

## Requirements

The DLL targets .NET Standard 2.1 for compatible Unity runtimes on Windows, Linux and macOS. Unity 6's referenced assemblies require 2.1. PNG capture uses Unity's GPU readback APIs and requires a graphics device supporting asynchronous readback. Linux/macOS execution has not yet been tested.

For the current video backend: Windows x64, Unity running Direct3D 11, an NVIDIA GPU with NVENC and a recent driver. Include `UnityMediaRecorder.dll`, [Direct3DVideoEncoder.dll](https://github.com/end3rbyte/Direct3DVideoEncoder) and [FFmpegMediaWriter.dll](https://github.com/end3rbyte/FFmpegMediaWriter).

Install FFmpeg separately and supply its executable path in `RecordingSettings.FfmpegPath`.

Follow the [FFmpeg download and setup instructions](https://github.com/end3rbyte/FFmpegMediaWriter#download-and-setup) and supply the executable path in `RecordingSettings.FfmpegPath`.

## Video example

Run this from your Unity component. `gameObject`, `camera` and `listener` belong to your scene. Create the output directory and use unused filenames. The FFmpeg path is only an example.

```csharp
using FFmpegMediaWriter;
using UnityEngine;
using UnityMediaRecorder;

var recorder = gameObject.AddComponent<global::UnityMediaRecorder.UnityMediaRecorder>();
recorder.RecordingCompleted += () => Debug.Log("MP4 ready");
recorder.RecordingFailed += error => Debug.LogException(error);

recorder.StartRecording(camera, listener, new RecordingSettings
{
    FfmpegPath = @"C:\tools\ffmpeg\bin\ffmpeg.exe",
    TemporaryContainerPath = @"C:\Captures\session.mkv.tmp",
    OutputPath = @"C:\Captures\session.mp4",
    Width = 3840,
    Height = 2160,
    MaximumFrameRate = 60,
    AntiAliasingSamples = 4,
    NativeEncodingPreset = 5,
    VideoStreamFormat = VideoStreamFormat.Hevc,
    FlipVertically = SystemInfo.graphicsUVStartsAtTop
});

// Later: stop capture, then wait for completion or failure.
recorder.StopRecording();
```

Keep the recorder, camera and listener alive through finalization. For two videos, use two recorder components. The FPS setting is a ceiling, not a guarantee.

Options in `RecordingSettings`:

- `VideoStreamFormat`: H.264 (library default) or HEVC. NVENC completion is asynchronous by default.
- `CaptureScreen = true`: record the application's displayed image, including UI. In the editor, this means the Game view, not editor panels.
- `GeneratePreviewImage = true` with `PreviewImagePath`: save a PNG just before video capture.
- `KeepIntermediateFile = true` with `ArchivePath`: keep the MKV after successful MP4 creation.

FFmpeg assembles encoded video and audio without recompressing video. Audio comes from Unity's mix.

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

Build with the .NET 10 SDK: `dotnet build UnityMediaRecorder.csproj -c Release -p:UnityManagedPath="YOUR_UNITY_MANAGED_DIRECTORY"`. Set `UnityManagedPath` to Unity's managed UnityEngine assembly directory. Output: `bin/Release/netstandard2.1/UnityMediaRecorder.dll`. Keep the three library repositories side by side for the project reference and native DLL copy. The current Direct3D/NVENC video backend remains Windows-only; Linux/macOS video recording requires a separately registered compatible backend.

To add a video engine, implement `VideoCaptureBackend`, validate codec selection in `ConfigureStreamFormat` and register a factory with `VideoCaptureBackendRegistry.Register`. Write packets through `VideoCaptureContext.WritePacket`, not directly to FFmpeg. No software encoding fallback is included.

See [UnitySample](https://github.com/end3rbyte/UnitySample) for an editable scene. Our code uses the [MIT license](LICENSE); third-party licenses and codec patent rights are separate.
