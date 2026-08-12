# Phase 2 delivery report

Status: **accepted** on 2026-07-24 for the stable DirectML/CPU inference engine.

## Delivered behavior

- `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 supplies DirectML and CPU EP from one native ORT build. The prior CPU-only 1.27.1 reference was removed to avoid loading conflicting native variants.
- DXGI discovery uses the same enumeration indices DirectML expects, rejects software and non-DirectX-12 adapters, and exposes stable LUID identity, dedicated video memory, display name, and current index.
- Auto orders supported DirectML devices by dedicated memory and then CPU. Explicit DirectML adapter selection retains CPU as the single fallback. Inference never installs components, calls `EnsureReady`, or accesses the network.
- DirectML sessions force `ORT_SEQUENTIAL`, disable memory patterns, enable full graph optimization, and serialize `Run`. CPU sessions detect eight physical cores on the acceptance workstation and reserve one for Windows/UI.
- A provider-aware lease cache keys model id/version/hash/path/provider/device, warms every new session, reuses matches, retains at most two entries, and evicts only idle LRU entries. Invalid GPU sessions are disposed after their last lease.
- GPU OOM and DXGI device-loss failures have stable typed categories/codes. The processor invalidates the failed GPU lease and retries the same preprocessed item once on CPU; CPU failures and non-eligible model/operator failures do not loop.
- `BenchmarkHardwareUseCase` and `LocalHardwareBenchmarkService` report warmed median inference, total time, throughput, working set, OS, runtime, model, and provider identity. BenchmarkDotNet covers decode, preprocess, tensor creation, inference, refinement, mask resize/composition, encode, total latency, managed allocation, and the required 512, 1080p, 12 MP, and 8K sizes.
- ModelTools now lists providers and runs hash-verified, provider-selectable, multi-iteration real-model smokes.

## Acceptance workstation results

Provider inventory:

| Provider | Device | DXGI index | Dedicated memory |
|---|---|---:|---:|
| DirectML | NVIDIA GeForce RTX 4060 Laptop GPU | 0 | 7,956 MiB |
| DirectML | Intel(R) UHD Graphics | 1 | 128 MiB |
| CPU | ONNX Runtime CPU | n/a | n/a |

Hash-verified U2NetP 1 warmed inference:

| Selection | Iterations | Median inference | Runtime fallback |
|---|---:|---:|---|
| Auto -> RTX 4060 DirectML | 5 | 43.6 ms | no |
| Explicit Intel UHD DirectML | 3 | 116.8 ms | no |
| Explicit CPU | 3 | 194.3 ms | no |

The Intel DirectML cold session preparation was materially slower than its warmed inference; sessions are therefore warmed and reused rather than recreated per image. The generated smoke PNGs were visually inspected.

The isolated BenchmarkDotNet short job used the deterministic 16x16 fixture model and reusable CPU `OrtValue` session: mean 28.34 microseconds, 1.1 KB managed allocation per operation. This fixture number validates the harness and allocation path; the real U2NetP numbers above are the hardware-selection measurements.

## Verification

Commands completed:

```text
.\.dotnet\dotnet.exe restore CutLocal.sln --locked-mode
.\.dotnet\dotnet.exe format CutLocal.sln --no-restore --verbosity minimal
.\.dotnet\dotnet.exe build CutLocal.sln -c Release --no-restore
.\.dotnet\dotnet.exe test CutLocal.sln -c Release --no-build --no-restore
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools ... -- providers
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools ... -- smoke ... --provider auto --iterations 5
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools ... -- smoke ... --provider cpu --iterations 3
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools ... -- smoke ... --provider directml --adapter 1 --iterations 3
.\.dotnet\dotnet.exe run --project tests\CutLocal.Benchmarks ... --filter *OrtValueInferenceBenchmarks* --job Short --cli .\.dotnet\dotnet.exe
```

- Release build: **0 warnings, 0 errors**.
- Final complete test run: **24/24** (14 unit, 8 integration, 1 golden, 1 stress), 0 skipped.
- Real model SHA-256: `309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8`.
- Production-path search and previous Phase 1 invariants still exclude Python, child process, local HTTP, and telemetry.

## Changed files

```text
Directory.Packages.props
.gitignore
README.md
assets/models/manifests/u2netp.json
docs/architecture.md
docs/failure-fallback.md
docs/model-evaluation.md
docs/phase-2-plan.md
docs/phase-2-report.md
docs/provider-selection.md
docs/test-matrix.md
src/CutLocal.Application/BenchmarkHardwareUseCase.cs
src/CutLocal.App/FileDialogService.cs
src/CutLocal.App/MainWindow.xaml
src/CutLocal.App/MainWindow.xaml.cs
src/CutLocal.App/MainWindowViewModel.cs
src/CutLocal.Contracts/ProcessingContracts.cs
src/CutLocal.Domain/ProcessingModels.cs
src/CutLocal.Inference/CpuTopology.cs
src/CutLocal.Inference/CutLocal.Inference.csproj
src/CutLocal.Inference/GpuFallbackPolicy.cs
src/CutLocal.Inference/IBackgroundRemovalModelAdapter.cs
src/CutLocal.Inference/IModelAdapterSessionCache.cs
src/CutLocal.Inference/InferenceException.cs
src/CutLocal.Inference/InferenceFailureClassifier.cs
src/CutLocal.Inference/ProviderSelectionService.cs
src/CutLocal.Inference/ProviderSessionOptions.cs
src/CutLocal.Inference/U2NetModelAdapter.cs
src/CutLocal.Inference/U2NetModelAdapterFactory.cs
src/CutLocal.Inference/WindowsInferenceProviderCatalog.cs
src/CutLocal.Infrastructure/LocalBackgroundRemovalProcessor.cs
src/CutLocal.Infrastructure/LocalHardwareBenchmarkService.cs
src/CutLocal.Infrastructure/PipelineLog.cs
src/CutLocal.Infrastructure/ServiceCollectionExtensions.cs
src/CutLocal.Imaging/SafePngDecoder.cs
src/CutLocal.Persistence/ModelManifestValidator.cs
tests/CutLocal.Benchmarks/CutLocal.Benchmarks.csproj
tests/CutLocal.Benchmarks/OrtValueInferenceBenchmarks.cs
tests/CutLocal.Benchmarks/PipelineStageBenchmarks.cs
tests/CutLocal.IntegrationTests/CutLocal.IntegrationTests.csproj
tests/CutLocal.IntegrationTests/FailurePathTests.cs
tests/CutLocal.IntegrationTests/GpuCpuFallbackPipelineTests.cs
tests/CutLocal.IntegrationTests/ProviderSessionCacheTests.cs
tests/CutLocal.UnitTests/CutLocal.UnitTests.csproj
tests/CutLocal.UnitTests/ProviderPolicyTests.cs
tests/CutLocal.GoldenImageTests/FixtureGoldenImageTests.cs
tools/CutLocal.ModelTools/ModelSmokeRunner.cs
tools/CutLocal.ModelTools/Program.cs
```

## Known risks and next boundary

- Windows ML provider catalog integration is deliberately not shipped because readiness/installation flows can download components. `WindowsMl` policy currently follows the documented offline fallback to DirectML then CPU. A future implementation must prove an already-ready, self-contained path without network mutation.
- Genuine driver removal and physical GPU OOM were not induced on the workstation. The exact runtime edge is covered end to end with a forced eligible DirectML failure and a real CPU OrtValue completion; native classification is covered separately.
- DirectML is in sustained engineering. The abstraction keeps WinML adoption possible without coupling domain/application code to provider APIs.
- Driver version is not yet included in persisted benchmark staleness keys because settings persistence is Phase 3/5 work.
- Full 512-through-8K BenchmarkDotNet sweeps are implemented but intentionally not all executed during this phase close; the isolated inference job and real U2NetP provider measurements were executed. Long memory/handle soaks remain Phase 6.
- The real U2NetP C#-versus-Python/rembg open-image corpus comparison is not closed. It remains a mandatory model-quality/commercial-release gate; Python will stay test-only.
- The local dependency-vulnerability audit was not rerun because the required external scan approval was rejected. It remains a mandatory Windows CI/release gate and is not represented as passed here.

Phase 3 may start from this accepted engine: professional WPF drag/drop, preview/before-after, provider/adapter settings, progress, cancellation, localization, and accessibility.
