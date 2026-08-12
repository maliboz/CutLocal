# Memory budget and pressure policy

## Budget model

Before decode, read dimensions from codec metadata using checked 64-bit arithmetic. Reject invalid dimensions, more than 100 million pixels by default, a side longer than 32,768 pixels, or an estimated live set that cannot fit the current budget. User-approved overrides remain capped by checked address-space and configured safety limits.

Estimated peak for one item is:

```text
decoded BGRA (W×H×4)
+ composed BGRA (W×H×4)
+ model input float (inputW×inputH×3×4)
+ model output/refined float (inputW×inputH×2×4)
+ codec/session safety allowance
```

For 7680×4320 this is approximately 253 MiB for two BGRA surfaces, 1.95 MiB for U2NetP tensor/masks, plus codec and session memory. This is why full-resolution previews are not retained and why GPU concurrency defaults to 1.

## Admission policy

- Queue capacity is bounded; files are paths and metadata, never predecoded bitmaps.
- GPU admission is one item per session. CPU starts at one and may benchmark up to two.
- Reserve the greater of 1 GiB or 20% of physical RAM for Windows, WPF, and native provider variance.
- Pause dequeuing when available physical memory falls below the reserve or predicted peak would cross 75% of physical RAM. Resume with hysteresis at 30% free.
- One decoded image, one composed output, and bounded model buffers are live per worker. Thumbnail/proxy ownership is separate.
- Tensor and mask memory comes from `MemoryPool<T>`; every item uses lexical `using`/`await using` ownership.
- Never call uncontrolled `GC.Collect()`. A GPU OOM evicts the GPU session before one CPU retry.

Phase 1 implements the decode bomb guard, pooled tensor/mask ownership, one worker, and deterministic disposal. Bounded channels, pressure pausing, adaptive concurrency, and proxy virtualization are Phase 4/6 acceptance work.
