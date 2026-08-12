# Phase 6 — resilience and long-running stability plan

## Objective

Harden the accepted offline PNG pipeline against long-running resource pressure, malformed inputs/models, storage failures, provider failure, repeated model switching, and Windows path edge cases. Production code must remain bounded and cancellation-aware; measurement-only forced garbage collection is allowed only inside stress tests.

## Production invariants

1. At most two warmed ONNX Runtime sessions may exist in the session cache.
2. A batch may admit at most the existing provider-safe concurrency, and admission pauses before decoding a new full-resolution item when runtime memory load is critical.
3. Every decoded bitmap, tensor owner, raw mask, refined mask, composed bitmap, session lease, stream, and partial file has an explicit owner and terminal cleanup path.
4. DirectML device loss or GPU out-of-memory may retry the item on CPU once. CPU failure never loops back to another provider retry.
5. Model quarantine retains at most three files per model/version and uses controlled, sanitized names.
6. Input/output/model paths remain Unicode-safe and support full paths longer than 300 characters under the application's `longPathAware` manifest.
7. No inference/startup network path, child-process inference, or committed model weight may be introduced.

## Resource budgets

| Scenario | Iterations | Session limit | Final working-set growth | Handle growth |
|---|---:|---:|---:|---:|
| Real small PNG → CPU ONNX → alpha PNG | 500 | 2 maximum; 1 expected | ≤ 96 MiB | ≤ 8 |
| Synthetic 4000×3000 decode/preprocess/CPU ONNX lifecycle | 100 | 2 maximum; 1 expected | ≤ 128 MiB | ≤ 8 |

The 4K scenario deliberately uses a lightweight compositor/writer after allocating and preprocessing the full-size bitmap. The 500-cycle scenario covers the real decoder, compositor, atomic PNG encoder, and file replacement path.

## Failure matrix

| Boundary | Required behavior |
|---|---|
| Truncated/invalid PNG | Typed `DecodeFailed`; no escaped bitmap |
| Locked input | Typed `FileLocked` |
| Locked output | Typed `FileLocked`; no `.partial` residue |
| Disk-full Windows codes | Typed `DiskFull` classifier for `0x70` and `0x27` |
| Decode/allocation memory failure | Typed `ImageTooLarge` / `PROC_MEMORY_PRESSURE` |
| Wrong model hash | Reject before session activation as `ModelCorrupted` |
| Corrupt ONNX with matching hash | Reject session creation as `ModelIncompatible` |
| DirectML device loss/GPU OOM | Invalidate GPU lease and acquire real CPU adapter once |
| Critical runtime memory load | Pause new batch admission; cancellation remains immediate |
| Repeated corrupt downloads | Retain only the newest three quarantine files per model/version |
| Repeated model switching | LRU cache never exceeds two native sessions |
| Unicode/long paths | Complete real CPU pipeline and atomically write output |

## Implementation slices

1. Close decode cancellation/allocation lifetime gaps and classify allocation pressure.
2. Make atomic-writer cleanup non-masking; preflight locked overwrite targets and expose deterministic disk-full code classification.
3. Add a runtime memory-pressure admission gate before each batch item enters the full-resolution pipeline.
4. Bound and sanitize model quarantine retention.
5. Extend provider fallback coverage to GPU OOM and repeated model/session switching.
6. Add corrupt model, locked file, Unicode, and 300+ character path integration tests.
7. Add isolated 500-cycle and 100-cycle resource stability suites with JSON evidence.
8. Run formatting, Release build, all test suites, manifest policy, source security scans, and hidden-host startup smoke.

## Acceptance criteria

- Release build completes with zero warnings and zero errors.
- All unit, integration, golden-image, and stress tests pass without skips.
- Both resource scenarios stay within the declared working-set, handle, and session budgets.
- No `.partial` residue remains after a locked-output failure.
- Corrupt image/model, allocation pressure, lock, disk-full code, GPU OOM, and device-loss paths return stable typed failures.
- Catalog/inference privacy boundaries remain unchanged and no model weight is committed.
- Phase 6 is documented and explicitly accepted before Phase 7 packaging/release work begins.
