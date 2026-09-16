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

The first NVENC asynchronous-completion experiment was run with Unity's `-batchmode` option. Its black frames came from cameras rendering zero frames, as confirmed by the render counters. That run cannot be used to accept or reject asynchronous NVENC. The experimental code was removed so the repository retains only the validated synchronous path.

## Next P5 optimization attempt

1. Add telemetry for Unity-rendered, GPU-copied, NVENC-submitted, NVENC-completed and muxed frames, plus queue depth and per-stage duration.
2. Reintroduce NVENC completion events behind an internal implementation switch while keeping P5, the existing owned texture ring and synchronous mode as the reference.
3. Run both modes in the visible player with two cameras, then verify extracted frames, duration, timestamps and all five frame counters.
4. Increase the owned input and output surface rings only if telemetry shows starvation while NVENC still has available throughput.
5. Share one native render-event scheduler across both sessions if Direct3D context contention remains measurable.

If Unity sustains 60 FPS but both NVENC completion rates remain below 60 FPS, the limiting factor is aggregate P5 capacity on the tested GPU rather than Unity synchronization. At that point, retaining P5 would require a different codec or NVENC configuration, a lower per-camera resolution or frame rate, or faster hardware; P4 remains the validated fallback rather than the desired final result.

Secondary improvements are pooled packet buffers, one shared audio producer and temporal-effect optimization guarded by visual regression comparisons.
