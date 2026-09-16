# UnityMediaRecorder

Set `RecordingSettings.CaptureScreen = true` to capture the application's final displayed frame, including UI, instead of a camera target. Supply the normal camera and audio listener arguments; the screen source does not change that camera's target. Capture runs at end-of-frame on the GPU and scales resized windows to the configured output dimensions. In the Unity editor this captures the Game view, not editor panels; keep the Game view visible.

See [PERFORMANCE.md](PERFORMANCE.md) for the measured bottlenecks, rejected experiments and optimization roadmap.

Reusable capture and recording orchestration for Unity applications.

The library captures video from Unity's normal camera render loop and captures the Unity audio mix. It delegates media transport, FFmpeg execution and output finalization to `FFmpegMediaWriter`. On Direct3D 11 and NVIDIA systems, it can use `Direct3DVideoEncoder.dll` for native H.264 encoding.

It does not depend on Valheim or BepInEx. The calling application owns user input, camera behavior, configuration, interface and output naming.

## API

Add `UnityMediaRecorder` to a Unity object, subscribe to its lifecycle events, then pass a camera, an audio listener and `RecordingSettings` to `StartRecording`. Call `StopRecording` to finish capture and create the MP4 file. Set `KeepIntermediateFile` to `true` only when the high-quality MKV archive must also be retained.

Set `GeneratePreviewImage` to `true` and provide `PreviewImagePath` to save the prepared camera frame as a PNG immediately before video capture begins.

Set `AntiAliasingSamples` to `1`, `2`, `4` or `8`. Multisampled camera output is resolved on the GPU before readback or native encoding.

`EncodingQuality` defaults to `Highest`, which maps to NVENC P5. Use `Balanced` for concurrent high-resolution recordings; the native backend maps it to P4 while retaining the same codec profile, bitrate and color metadata. Other backends may interpret this backend-neutral preference as appropriate.

The main public API is `UnityMediaRecorder`, `RecordingSettings` and `MediaRecorderLog`. Pipes and FFmpeg processes remain internal.

## PNG image sequences

Use `StartPngSequence` when individual lossless frames are needed instead of a video. The capture frequency can be lower or higher than one image per second and follows a wall-clock schedule, capped by the rate at which Unity renders frames. Stop the sequence with `StopPngSequence`.

```csharp
recorder.StartPngSequence(camera, new PngSequenceSettings
{
    OutputDirectory = @"C:\Captures\Sequence",
    FileNamePrefix = "frame_",
    Width = 3840,
    Height = 2160,
    CapturesPerSecond = 2.0,
    InitialDelaySeconds = 0.0,
    AntiAliasingSamples = 4
});

// Later: writes no more images and raises RecordingCompleted.
recorder.StopPngSequence();
```

Files are named `frame_000000.png`, `frame_000001.png`, and so on. GPU readback is asynchronous, while PNG compression and disk writes run on a bounded background queue. `InitialDelaySeconds` can stagger several cameras so they do not request readback in the same rendered frame. This mode captures no audio and requires neither FFmpeg nor NVENC.

## Video backends

Video capture and encoding are replaceable through `VideoCaptureBackend`. A backend declares whether it sends H.264 or HEVC data, receives an immutable `VideoCaptureContext`, and writes timestamped encoded packets through that context. It never accesses the recorder's pipes or FFmpeg process directly.

Register a factory before starting a recording:

```csharp
VideoCaptureBackendRegistry.Register(
    host => MyEncoder.IsAvailable
        ? host.AddComponent<MyEncoder>()
        : null,
    priority: 200);
```

The factory returns `null` when its engine is unavailable. Higher priorities are selected first. The built-in D3D11/NVENC backend has priority `100`. There is no built-in video encoding fallback: recording reports an explicit error if no compatible backend is available. PNG capture remains independent. A custom backend needs no change in `UnityMediaRecorder`.

The optional prepared `RenderTexture` passed to `StartRecording` remains owned by the caller. The selected backend may use it during capture but must not release or destroy it.

The source files are grouped by domain: `Capture` contains Unity capture and video backends, while `Pipeline` contains only Unity recording orchestration and configuration.

## Example

The standalone [UnitySample](https://github.com/landoria-gaming/UnitySample) repository builds a lit cube scene entirely from code, with moving and fixed cameras. It demonstrates PNG sequences and native video recording. It is not included in the library repository.

## Build

Build `UnityMediaRecorder.csproj` with .NET Framework 4.8. Set the `UnityManagedPath` MSBuild property to the directory containing the Unity managed assemblies.

## Runtime requirements

- Unity with the required managed modules
- `FFmpegMediaWriter.dll`
- FFmpeg installed separately for multiplexing and audio encoding
- `Direct3DVideoEncoder.dll` for the optional native NVIDIA path

Released under the [MIT License](LICENSE).
