# Unity/BepInEx integration example

`UnityOrbitCapture.cs` is a minimal Unity-only example. It creates a shadow-casting cube, shadow-receiving floor, directional light, wind-animated tall-grass patches, softly drifting airborne particles, low humidity mist, depth fog and an orbiting camera at runtime. It enables vertical synchronization and overlays live render time, capture resolution, target rate, elapsed time, rendered-frame count and GPU name. It captures both cameras as PNG image sequences for ten seconds in `Desktop/UnityMediaRecorderExample`.

Each camera produces one PNG per second. The numbered sequences are stored in separate `<capture name>_Frames` directories; the example does not create MP4 or MKV files.

## Run it

1. Create an empty 3D Unity project compatible with the Unity assemblies used to build `Landoria.UnityMediaRecorder`.
2. Ensure Unity's built-in Particle System module is enabled in Package Manager.
3. Copy `Landoria.UnityMediaRecorder.dll` and `Landoria.FFmpegMediaWriter.dll` into `Assets/Plugins`.
4. Copy the complete `Example` folder into `Assets`.
5. Open an empty scene and enter Play mode.

No scene objects, inspector configuration, FFmpeg installation or native NVIDIA DLL are required for the PNG example.

The standalone example also accepts `CAPTURE_WIDTH`, `CAPTURE_HEIGHT`, `CAPTURE_FRAME_RATE`, `CAPTURE_MSAA` and `CAPTURE_PROFILE` environment variables. MSAA defaults to 4 samples. These overrides make it possible to generate profiles such as Full HD/30 FPS and 4K/60 FPS from the same build.

Set `CAPTURE_BENCHMARK_MODE` to `single-render`, `dual-render`, `dual-nvenc` or `dual-nvenc-no-temporal` to run a ten-second controlled comparison instead of the PNG example. Each run writes one `CAPTURE_BENCHMARK` log entry containing the exact rendered-frame counts and average FPS. The NVENC modes require FFmpeg and the native encoder DLL.
