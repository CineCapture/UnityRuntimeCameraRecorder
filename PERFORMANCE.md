# Performance roadmap

The current reference workload records two independent 3840 x 2160 cameras at a 60 FPS target, with 4x MSAA, temporal processing, H.264 High 4:2:0 and separate NVENC sessions.

Observed results show that NVENC usually encodes every submitted frame. The limiting stage is therefore the production and transfer of rendered frames rather than packet encoding.

## Prioritized improvements

### 1. Resolve directly into an NVENC surface ring

The current native path resolves each multisampled Unity target into a single-sample texture and then copies that texture into an internally registered NVENC surface.

Expose a ring of native encoder surfaces to the Unity backend and resolve directly into the next available surface. This removes one full-resolution GPU copy per camera and frame while retaining asynchronous encoding.

The implementation must keep a surface unavailable until NVENC has consumed it. It should drop a frame instead of blocking Unity when the ring is exhausted.

### 2. Produce NV12 surfaces on the GPU

Unity currently supplies RGBA textures while the target H.264 stream is YUV 4:2:0. A compute shader or native Direct3D conversion pass could write NV12 surfaces before submission.

This reduces surface bandwidth and makes color conversion, range and BT.709 coefficients explicit. The conversion should be validated against the reference video before replacing the driver-managed conversion.

### 3. Share one native render-event scheduler

Multiple encoder instances currently issue independent Unity plugin events. A shared scheduler could receive all camera submissions for a frame and dispatch their copies in one render-thread event.

This should reduce context transitions and make multi-camera ordering deterministic without coupling the independent encoder sessions.

### 4. Optimize temporal processing without changing its output

The temporal pass reads several full-resolution samples plus motion vectors and history for every camera. Candidate internal improvements include a compute implementation, tiled processing, half-precision intermediates and adaptive sampling for pixels with negligible motion.

Visual regression frames must be compared before accepting this work because aggressive sample reduction can change the intended result.

### 5. Remove managed packet allocations

Each encoded packet is currently copied into a new managed byte array. Use pooled buffers, unmanaged spans or a native writer bridge to reduce garbage collection and memory bandwidth.

This is expected to improve frame-time stability more than average GPU throughput.

### 6. Capture audio once and fan it out

Two recorders currently attach two audio capture components to the same Unity mix. A shared audio producer could distribute one captured buffer to multiple media writers.

This removes duplicate audio-thread conversion and allocation work while preserving identical audio in every output.

### 7. Add stage-level telemetry

Record GPU render duration, MSAA resolve duration, temporal-pass duration, render-event copy duration, encoder queue depth, NVENC duration, packet queue depth and FFmpeg finalization duration independently.

These measurements should be included in repeatable single-camera and dual-camera benchmarks before making further architectural changes.

## Tested approaches not retained

### D3D11 fences

Waiting for `ID3D11Fence` events from the encoder workers blocked startup with the Direct3D context supplied by the tested Unity player. The implementation was removed.

### Query completion on the render thread

Moving query completion entirely into later Unity render events was stable but delayed surface reuse and reduced both outputs to approximately 30 FPS. The original asynchronous worker polling was restored.

## Lower-cost fallbacks

If identical 4K output is not mandatory, the largest immediate gains remain reducing the secondary camera resolution, disabling its unnecessary camera-motion blur, or lowering its MSAA level. These are configuration tradeoffs rather than architecture optimizations and are intentionally not part of the primary roadmap.
