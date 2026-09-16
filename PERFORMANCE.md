# Performance notes

The reference workload records two independent 3840 x 2160 cameras with a 60 FPS target, 4x MSAA, temporal processing, H.264 High 4:2:0 and separate NVENC sessions.

## Measured result

Two simultaneous P5 sessions rendered at approximately 41 FPS on the test system. Selecting the backend-neutral `Balanced` quality setting, which maps to NVENC P4, increased the same workload to approximately 56.6 rendered FPS while retaining the 67.2 Mbit/s target, 30-frame GOP, High profile and BT.709 metadata. A single recording continues to use `Highest`/P5 by default.

The standalone test player must remain visible. Unity's `-batchmode` option disables camera rendering in this example and therefore produces black video; it is not a valid performance benchmark.

## Tested approaches not retained

- NVENC asynchronous completion events removed most render-thread contention but produced black frames with the tested Unity Direct3D 11 textures and driver.
- Resolving directly into externally wrapped NVENC textures was incompatible with the scene's post-processing path and changed the image.
- Explicit GPU RGB-to-NV12 conversion did not improve throughput.
- Reducing MSAA from 4x to 1x improved the dual P5 workload by only about 1.3 FPS.
- Waiting for Direct3D fences blocked startup with Unity's immediate context.
- Delaying query completion to later Unity render events reduced both outputs to approximately 30 FPS.

## Remaining candidates

1. Share one native render-event scheduler across encoder instances.
2. Remove managed packet allocations with pooled or native buffers.
3. Capture audio once and fan it out to multiple media writers.
4. Add stage-level GPU, queue-depth and FFmpeg telemetry.
5. Optimize the temporal effect only with visual regression tests.
