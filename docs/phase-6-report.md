# Phase 6 — resilience and stability report

## Outcome

Phase 6 is complete. CutLocal now pauses new batch admission under critical local memory pressure, contains corrupt-model quarantine growth, preserves typed failure semantics across lock/disk/memory/model/provider faults, supports Unicode paths longer than 300 characters, and has repeatable resource-stability evidence for both complete small-image processing and full-resolution 4K lifecycles.

## Delivered hardening

- `SafePngDecoder` now owns its bitmap until a successful `DecodedImage` transfer. Cancellation, codec failure, I/O failure, and allocation failure all dispose the temporary bitmap. Allocation pressure maps to `ImageTooLarge` / `IMG_MEMORY_PRESSURE`.
- `LocalBackgroundRemovalProcessor` maps otherwise escaping allocation pressure to `ImageTooLarge` / `PROC_MEMORY_PRESSURE` instead of `Unknown`.
- `AtomicPngWriter` probes an existing overwrite target so Windows sharing locks become deterministic `FileLocked` failures. Its unique `.partial` cleanup can no longer replace the original exception, and Windows disk-full/handle-disk-full codes map to `DiskFull`.
- `LocalMemoryPressureGate` checks `GCMemoryInfo` before a queued batch item is decoded. It pauses at the existing item boundary when the runtime high-load threshold is crossed or available reserve falls below 256 MiB, polls every 250 ms, and remains cancellation-aware. It never forces garbage collection in production.
- Model quarantine names sanitize model/version/reason segments. Retention is best effort and bounded to the newest three files per model/version, preventing repeated corrupt transfers from growing storage without limit.
- DirectML device loss and GPU out-of-memory both invalidate the GPU lease and retry once on a real CPU adapter. Existing policy prevents a second fallback loop.
- Thirty repeated switches among three descriptor versions kept the LRU session cache at its two-session ceiling.
- A real fixture model, input PNG, and output PNG completed over a Unicode path longer than 300 characters. The application manifest remains `longPathAware`.
- Existing interruption recovery, pause/resume, active-and-queued cancellation, atomic job persistence, two-session LRU, decompression-bomb limits, and provider concurrency clamps remained enabled and passed regression.

## Release resource evidence

The stress assembly disables test parallelism so other test classes do not distort process-level measurements. Ten small-image or five 4K warm-up cycles run before the baseline. Forced collections are measurement-only test operations and are absent from production code.

| Scenario | Result | Elapsed | Working set | Handles | Sessions |
|---|---|---:|---:|---:|---:|
| 500 real PNG/CPU ONNX/alpha-PNG cycles | PASS | 4478.19 ms | 98,754,560 → 107,061,248 bytes (`+7.92 MiB`), peak 107,061,248 | 429 → 433 (`+4`) | 1 |
| 100 synthetic 4000×3000 decode/preprocess/CPU ONNX cycles | PASS | 1183.18 ms | 109,654,016 → 105,062,400 bytes (`−4.38 MiB`), peak 111,132,672 | 435 → 435 (`0`) | 1 |

Both scenarios are comfortably within the declared limits: 96/128 MiB final working-set growth, eight handles, and two cached sessions. Samples stabilized rather than increasing linearly.

Evidence files:

- `%TEMP%\CutLocal.Tests\phase-6-small-pipeline-stability.json`
- `%TEMP%\CutLocal.Tests\phase-6-4k-lifecycle-stability.json`

## Failure and compatibility evidence

| Check | Result |
|---|---|
| Truncated PNG | Typed `DecodeFailed` |
| Locked PNG input | Typed `FileLocked` / `IMG_READ_IO` |
| Locked overwrite output | Typed `FileLocked` / `ENC_OUTPUT_IO`; no partial file |
| Disk-full codes | `0x80070070` and `0x80070027` recognized |
| Decode allocation failure | Typed `ImageTooLarge` / `PROC_MEMORY_PRESSURE` |
| Wrong model SHA-256 | `ModelCorrupted` / `MODEL_SHA256_MISMATCH` before activation |
| Corrupt ONNX with matching SHA-256 | `ModelIncompatible` / `MODEL_SESSION_CREATE` |
| DirectML device loss | One successful CPU fallback |
| DirectML GPU OOM | One successful CPU fallback |
| Repeated corrupt download | Exactly three quarantine files retained after four attempts |
| Repeated model switching | Cache stayed at two sessions or fewer |
| Unicode 300+ character path | Real CPU inference and PNG output passed |
| Critical-memory batch admission | Processor was not entered; cancellation completed the batch |

## Final verification

- Release build: success, `0` warnings, `0` errors.
- Complete regression suite: `88/88` passed, `0` skipped.
  - Unit/UI: `67/67`
  - Integration: `17/17`
  - Golden image: `1/1`
  - Stress: `3/3`
- Formatter verification: `dotnet format --verify-no-changes` passed.
- Historical manifest result: BiRefNet General Lite was accepted under the
  former MIT-weight assumption. Current policy classifies that weight as
  `LicenseRef-BiRefNet-Weights-NonCommercial` and never bundles it. U2NetP
  (`1 MiB`, Apache-2.0) remains accepted.
- Hidden Release process-liveness smoke: the process remained alive after four seconds and measured a 19,107,840-byte working set with 193 handles. This historical check did not prove that WPF created a window; Phase 7 replaced it with a real non-zero main-window-handle and responsiveness check after fixing the startup dispatcher deadlock.
- Source security scan:
  - `HttpClient` exists only in `ModelPackageManager` registration/implementation;
  - no child-process inference API or `NamedOnnxValue` path exists;
  - the only `rembg` source match is an adapter-compatibility comment;
  - no `.onnx`, `.ort`, `.pt`, `.pth`, `.ckpt`, or `.safetensors` weight is committed outside build outputs;
  - ONNX Runtime telemetry remains explicitly disabled.

## Commands executed

```powershell
.\.dotnet\dotnet.exe format CutLocal.sln --no-restore --verbosity minimal
.\.dotnet\dotnet.exe format CutLocal.sln --verify-no-changes --no-restore --verbosity minimal
.\.dotnet\dotnet.exe build CutLocal.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.UnitTests\CutLocal.UnitTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.IntegrationTests\CutLocal.IntegrationTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.GoldenImageTests\CutLocal.GoldenImageTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.StressTests\CutLocal.StressTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools\CutLocal.ModelTools.csproj --configuration Release --no-build -- assets\models\manifests
```

Additional controlled PowerShell checks covered source network/process/weight boundaries, evidence extraction, and the four-second hidden Release host smoke.

## Changed files

Production:

- `src/CutLocal.Contracts/IMemoryPressureGate.cs`
- `src/CutLocal.Application/ProcessBatchUseCase.cs`
- `src/CutLocal.Imaging/SafePngDecoder.cs`
- `src/CutLocal.Imaging/AtomicPngWriter.cs`
- `src/CutLocal.Infrastructure/LocalMemoryPressureGate.cs`
- `src/CutLocal.Infrastructure/LocalBackgroundRemovalProcessor.cs`
- `src/CutLocal.Infrastructure/ModelPackageManager.cs`
- `src/CutLocal.Infrastructure/ServiceCollectionExtensions.cs`

Tests and documentation:

- `tests/CutLocal.UnitTests/LocalMemoryPressureGateTests.cs`
- `tests/CutLocal.UnitTests/ProcessBatchUseCaseTests.cs`
- `tests/CutLocal.UnitTests/ModelPackageManagerTests.cs`
- `tests/CutLocal.IntegrationTests/AtomicPngWriterTests.cs`
- `tests/CutLocal.IntegrationTests/CpuInferenceTests.cs`
- `tests/CutLocal.IntegrationTests/FailurePathTests.cs`
- `tests/CutLocal.IntegrationTests/GpuCpuFallbackPipelineTests.cs`
- `tests/CutLocal.IntegrationTests/ProviderSessionCacheTests.cs`
- `tests/CutLocal.StressTests/CutLocal.StressTests.csproj`
- `tests/CutLocal.StressTests/PipelineResourceStabilityTests.cs`
- `tests/CutLocal.StressTests/TestAssembly.cs`
- `docs/test-matrix.md`
- `docs/phase-6-plan.md`
- `docs/phase-6-report.md`

## Deliberate limits and remaining release work

- Disk-full acceptance uses deterministic Windows error-code injection; it does not intentionally fill a user volume.
- GPU OOM/device-loss pipeline tests inject the typed native-failure result and then run the real CPU adapter. Representative AMD/Intel/NVIDIA hardware fault testing remains a pre-release device-matrix task.
- The 4K stress allocates and preprocesses the real 4000×3000 bitmap and runs CPU ONNX, then uses a lightweight compositor/writer to isolate ownership stability. The separate 500-cycle test exercises the complete real compositor and atomic writer.
- Working-set values are machine/run specific; the committed assertions enforce bounded deltas rather than absolute memory numbers.
- PNG remains the accepted input/output vertical slice. Additional formats require their own decoder, encoder, metadata, and fuzz acceptance.

## Next phase

Phase 7 should cover packaging and release readiness: installer/upgrade/uninstall behavior, default/optional model acquisition policy, code signing and provenance, clean-machine offline/online first run, representative GPU/Windows device matrix, accessibility/keyboard/manual DPI QA, and final distribution artifacts. Phase 7 should not weaken any Phase 1–6 security, offline, resource, or typed-failure invariant.
