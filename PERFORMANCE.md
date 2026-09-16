# Performance notes

The target workload records two independent 3840 x 2160 cameras at 60 FPS, with 4x MSAA, temporal processing, H.264 High 4:2:0, NVENC P5 and separate encoder sessions. Reaching that target without reducing image quality remains the primary performance goal.

## Measured result

Two simultaneous P5 sessions rendered at approximately 41 FPS on the test system. Selecting the backend-neutral `Balanced` quality setting, which maps to NVENC P4, increased the same workload to approximately 56.6 rendered FPS while retaining the 67.2 Mbit/s target, 30-frame GOP, High profile and BT.709 metadata. A single recording continues to use `Highest`/P5 by default.

The standalone test player must remain visible. Unity's `-batchmode` option disables camera rendering in this example and therefore produces black video; it is not a valid performance benchmark.

## Tested approaches not retained

- Resolving directly into externally wrapped NVENC textures was incompatible with the scene's post-processing path and changed the image.
- Explicit GPU RGB-to-NV12 conversion did not improve throughput.
- Reducing MSAA from 4x to 1x improved the dual P5 workload by only about 1.3 FPS.
- Waiting for Direct3D fences blocked startup with Unity's immediate context.
- Delaying query completion to later Unity render events reduced both outputs to approximately 30 FPS.

## Invalid asynchronous test

The first NVENC asynchronous-completion experiment was run with Unity's `-batchmode` option. Its black frames came from cameras rendering zero frames, as confirmed by the render counters. That run cannot be used to accept or reject asynchronous NVENC. That code was removed at the time. A new opt-in implementation is described below; synchronous completion remains the default.

## Next P5 optimization attempt

1. Add telemetry for Unity-rendered, GPU-copied, NVENC-submitted, NVENC-completed and muxed frames, plus queue depth and per-stage duration.
2. Reintroduce NVENC completion events behind an internal implementation switch while keeping P5, the existing owned texture ring and synchronous mode as the reference.
3. Run both modes in the visible player with two cameras, then verify extracted frames, duration, timestamps and all five frame counters.
4. Increase the owned input and output surface rings only if telemetry shows starvation while NVENC still has available throughput.
5. Share one native render-event scheduler across both sessions if Direct3D context contention remains measurable.

If Unity sustains 60 FPS but both NVENC completion rates remain below 60 FPS, the limiting factor is aggregate P5 capacity on the tested GPU rather than Unity synchronization. At that point, retaining P5 would require a different codec or NVENC configuration, a lower per-camera resolution or frame rate, or faster hardware; P4 remains the validated fallback rather than the desired final result.

Secondary improvements are pooled packet buffers, one shared audio producer and temporal-effect optimization guarded by visual regression comparisons.

## Visible asynchronous test — 2026-09-16

The experimental NVIDIA implementation separates submission and output completion into two workers, with registered Windows completion events. Both synchronous and asynchronous paths now use four owned surfaces, the NVIDIA minimum with zero B-frames. Configuration and image processing are unchanged.

On the RTX 5060, two 4K/P5 cameras with VSync and MSAA 4x were recorded for ten seconds in a visible player:

- Synchronous: 39.17 Unity render FPS; 361 and 300 completed native frames.
- Asynchronous, repeated after fixing EOS event handling: about 59 Unity render FPS; 441 and 433 completed native frames, with 166 and 174 requests dropped because the four surfaces were busy. Every copied frame was submitted and completed. No native error was logged in the corrected run.

This improves rendering responsiveness, but does **not** achieve two 60 FPS videos. Output verification must use decoded frame counts, not just the 60 FPS setting or Unity render counters. Aggregate P5 hardware saturation is not yet proven: GPU-copy waits (~19 ms) and completion waits (~23 ms) still require investigation. These are CPU wall-clock waits, not GPU execution measurements, and overlap across workers.

Enable the experiment before launching the application in PowerShell:

```powershell
$env:DIRECT3D_NVENC_ASYNC = '1'
```

Use `'0'` or remove the variable to return to synchronous completion. The switch belongs to the NVIDIA backend, not the reusable recorder configuration. Native telemetry is logged as `NATIVE_PIPELINE` JSON and retained by `UnityMediaRecorder.LastVideoDiagnosticsJson` after stopping. It reports requests, copies, submissions, completions, drops, peak queue depths and stage averages. This is backend telemetry; muxed counts still require ffprobe.

Next controlled test: increase the owned surface ring identically in both modes, measure surface starvation and GPU/context contention, and verify decoded frames and timestamps before changing the default. Reference: [NVIDIA Video Codec SDK 13.1 programming guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/nvenc-video-encoder-api-prog-guide/index.html).
