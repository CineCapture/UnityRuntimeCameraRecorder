# Unity/BepInEx integration example

`UnityOrbitCapture.cs` is a minimal Unity-only example. It creates a shadow-casting cube, shadow-receiving floor, directional light, wind-animated tall-grass patches, softly drifting airborne particles, low humidity mist, depth fog and an orbiting camera at runtime. It enables vertical synchronization and overlays live render time, capture resolution, target rate, selected backend, elapsed time, rendered-frame count and GPU name. It saves the exact prepared camera frame as a PNG immediately before capture and records five seconds to `Desktop/UnityMediaRecorderExample`.

## Run it

1. Create an empty 3D Unity project compatible with the Unity assemblies used to build `Landoria.UnityMediaRecorder`.
2. Ensure Unity's built-in Particle System module is enabled in Package Manager.
3. Copy `Landoria.UnityMediaRecorder.dll` and `Landoria.FFmpegMediaWriter.dll` into `Assets/Plugins`.
4. Optionally copy `Landoria.D3D11NvencEncoder.dll` into `Assets/Plugins/x86_64` for the native NVIDIA path.
5. Copy the complete `Example` folder into `Assets`.
6. Install FFmpeg and make `ffmpeg.exe` available through `PATH`, or set the `FFMPEG_PATH` environment variable to its complete path.
7. Open an empty scene and enter Play mode.

No scene objects or inspector configuration are required. Without the native NVIDIA DLL, the recorder automatically uses its portable Unity GPU-readback backend.

The standalone example also accepts `CAPTURE_WIDTH`, `CAPTURE_HEIGHT`, `CAPTURE_FRAME_RATE`, `CAPTURE_MSAA` and `CAPTURE_PROFILE` environment variables. MSAA defaults to 4 samples. These overrides make it possible to generate profiles such as Full HD/30 FPS and 4K/60 FPS from the same build.
