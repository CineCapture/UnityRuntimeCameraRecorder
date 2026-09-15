# Landoria.UnityMediaRecorder

See [PERFORMANCE.md](PERFORMANCE.md) for the measured bottlenecks, rejected experiments and optimization roadmap.

Reusable capture and recording orchestration for Unity applications.

The library captures video from Unity's normal camera render loop and captures the Unity audio mix. It delegates media transport, FFmpeg execution and output finalization to `Landoria.FFmpegMediaWriter`. On Direct3D 11 and NVIDIA systems, it can use `Landoria.D3D11NvencEncoder.dll` for native H.264 encoding.

It does not depend on Valheim or BepInEx. The calling application owns user input, camera behavior, configuration, interface and output naming.

## API

Add `UnityMediaRecorder` to a Unity object, subscribe to its lifecycle events, then pass a camera, an audio listener and `RecordingSettings` to `StartRecording`. Call `StopRecording` to finish capture and create the MP4 file. Set `KeepIntermediateFile` to `true` only when the high-quality MKV archive must also be retained.

Set `GeneratePreviewImage` to `true` and provide `PreviewImagePath` to save the prepared camera frame as a PNG immediately before video capture begins.

Set `AntiAliasingSamples` to `1`, `2`, `4` or `8`. Multisampled camera output is resolved on the GPU before readback or native encoding.

The main public API is `UnityMediaRecorder`, `RecordingSettings` and `MediaRecorderLog`. Pipes and FFmpeg processes remain internal.

## Video backends

Video capture and encoding are replaceable through `VideoCaptureBackend`. A backend declares whether it sends raw RGBA, H.264 or HEVC data, receives an immutable `VideoCaptureContext`, and writes frames or timestamped encoded packets through that context. It never accesses the recorder's pipes or FFmpeg process directly.

Register a factory before starting a recording:

```csharp
VideoCaptureBackendRegistry.Register(
    host => MyEncoder.IsAvailable
        ? host.AddComponent<MyEncoder>()
        : null,
    priority: 200);
```

The factory returns `null` when its engine is unavailable. Higher priorities are selected first. The built-in D3D11/NVENC backend has priority `100`; the Unity GPU-readback fallback has the lowest possible priority. A custom backend therefore needs no change in `UnityMediaRecorder`.

The optional prepared `RenderTexture` passed to `StartRecording` remains owned by the caller. The selected backend may use it during capture but must not release or destroy it.

The source files are grouped by domain: `Capture` contains Unity capture and video backends, while `Pipeline` contains only Unity recording orchestration and configuration.

## Example

The [Unity/BepInEx integration example](Example/README.md) builds a lit cube scene entirely from code, moves a camera around it and records the result as a five-second video.

## Build

Build `Landoria.UnityMediaRecorder.csproj` with .NET Framework 4.8. Set the `UnityManagedPath` MSBuild property to the directory containing the Unity managed assemblies.

## Runtime requirements

- Unity with the required managed modules
- `Landoria.FFmpegMediaWriter.dll`
- FFmpeg installed separately for multiplexing and audio encoding
- `Landoria.D3D11NvencEncoder.dll` for the optional native NVIDIA path

Released under the [MIT License](LICENSE).
