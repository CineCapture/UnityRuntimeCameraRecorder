# Performance notes

The target workload records two independent 3840 x 2160 cameras at 60 FPS, with 4x MSAA, temporal processing, H.264 High 4:2:0, NVENC P5 and separate encoder sessions. Reaching that target without reducing image quality remains the primary performance goal.

Latest result: asynchronous HEVC Main/P5 sustained approximately 60 encoded FPS per camera in 10- and 30-second visible tests, without capture drops. H.264 remains the default and its dual-P5 throughput remains ~43 FPS per camera. Details, experimental activation and remaining quality/compatibility/frame-pacing checks appear below.

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

### Eight-surface experiment — not retained

With eight surfaces per session, the same visible dual-camera 4K/P5/60/VSync/MSAA4 run rendered 601 frames per camera in 10.248 seconds (~58.65 FPS). Native completions were 445 and 439, versus 441 and 433 with four surfaces; this single short comparison does not establish a sustained throughput improvement. Surface drops remained 163 and 169. Peak completion queue depth grew from 3 to 7, while GPU-copy waits remained ~19 ms and completion waits ~23 ms. No native error was logged.

The additional surfaces mainly allowed more frames to wait in flight rather than removing the bottleneck. Four surfaces were restored to avoid unnecessary memory use and backlog. The next investigation should distinguish Direct3D immediate-context/copy scheduling contention from aggregate NVENC throughput, preferably through GPU tracing and a shared native scheduling experiment. Buffer starvation alone is a symptom here; these CPU wait measurements do not prove which GPU stage is limiting throughput.

### Copy-query control and encoder utilization

An experimental `GetData(D3D11_ASYNC_GETDATA_DONOTFLUSH)` polling path, with a 100 ms fallback allowing command submission, completed 438 and 435 frames in the dual-camera workload (~43 FPS each). No fallback was needed; GPU-copy and completion waits remained ~19 and ~23 ms. This provided no meaningful gain and the code was removed. See [Microsoft's GetData flag documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_async_getdata_flag).

With ordinary queries and only camera 1 recorded, both scene cameras still rendered 600 frames; the native session completed all 606 requests without drops. A repeated dual-recording control completed 441 and 432 frames with 166 and 175 drops.

During that dual control, `nvidia-smi --query-gpu=utilization.gpu,utilization.encoder --format=csv,noheader`, sampled approximately once per second, consistently reported encoder utilization at 100% while overall GPU utilization was 49–55%. A single incidental sample during the single-camera run reported encoder utilization at 68%; it is not a time-series baseline. Together with the single-camera success and unchanged throughput after enlarging the ring and changing query behavior, these observations strongly implicate aggregate P5 encoding capacity in the current configuration. This is evidence, not proof that every possible P5 configuration has the same limit; no GPU trace was captured.

Keep asynchronous completion for rendering responsiveness. Prioritize profiling and controlled NVENC configuration/codec experiments over adding surfaces or implementing a shared scheduler without evidence. Retaining the exact current dual-4K/P5 configuration at 60 encoded FPS is not established as achievable on this RTX 5060. Any codec or encoding-feature change needs separate quality and compatibility validation, not an automatic silent quality reduction.

### P5 low-latency tuning — not retained

Changing only NVENC tuning from HIGH_QUALITY to LOW_LATENCY for preset lookup and initialization, while retaining P5, H.264, 67.2 Mbit/s CBR, GOP30, no B-frames and single pass, completed 443 and 431 frames over ~10.14 seconds. Unity rendered ~59.05 FPS; native drops remained 163 and 175. No native error was logged. This was no meaningful throughput gain over HIGH_QUALITY, so the experimental tuning switch was removed. No equivalent-quality claim is made: tuning changes can affect preset-derived encoding features.

### HEVC Main/P5 — successful throughput experiment

Selecting HEVC Main instead of H.264 High while retaining P5/HIGH_QUALITY, 67.2 Mbit/s CBR per video, GOP30, no B-frames, single pass, 8-bit 4:2:0 and limited BT.709 produced two successful visible runs:

- Ten seconds: 608 native requests, copies, submissions and completions per camera; zero drops. Decoded MP4 counts were also 608 each, over 10.145 and 10.153 seconds (~59.9 FPS). ffprobe confirmed HEVC Main, 3840x2160, yuv420p, limited BT.709. Unity rendered ~59.43 FPS. Encoder utilization samples were 79–94%.
- Thirty seconds: 1809 requests, copies, submissions and completions per camera; zero drops. Both MP4s also decoded to 1809 frames over 30.157 and 30.160 seconds (~59.99 and ~59.98 FPS). Unity rendered 1800 frames per camera at ~59.81 FPS. Capture duration was 30.096 seconds. Native errors were absent. Packet PTS were strictly increasing; maximum gaps were 78 ms and 51 ms, so average 60 FPS does not establish perfectly uniform frame pacing. Investigate those occasional timing gaps separately.

An extracted frame was visually checked for orientation and non-black scene content. This is not a lossless-reference or objective compression-quality comparison; same bitrate and preset across codecs do not prove identical quality. Compatibility and audio synchronization still need testing in the user's target players. The codec stays opt-in; H.264 remains the default.

Use matching rebuilt Direct3DVideoEncoder and UnityMediaRecorder binaries, and set both variables **before** launching the sample:

```powershell
$env:DIRECT3D_NVENC_ASYNC = '1'
$env:DIRECT3D_NVENC_HEVC = '1'
.\Builds\Windows\UnitySample.exe --render 4k --resolution 4k --fps 60 --vsync on --aa 4 --preset p5 --record camera1,camera2 --duration 30 --quit-after-recording
```

Set `DIRECT3D_NVENC_HEVC` to `'0'` or remove it to restore H.264. Do not change the variable during a session: the recorder describes the stream to the writer before the native encoder initializes. The native backend logs its selected codec, preset and fixed encoding configuration inside `NATIVE_PIPELINE.encoder`. A future supported public codec selection should negotiate an immutable stream format rather than relying on this process-level experimental switch. FFmpeg still only multiplexes the native video packets; it does not recompress video.
