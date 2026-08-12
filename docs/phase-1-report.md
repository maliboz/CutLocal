# Phase 1 delivery report

Status: **accepted** on 2026-07-24 for the Phase 1 CPU/PNG vertical slice.

## Delivered behavior

- A minimal WPF/MVVM surface selects one PNG, processes it asynchronously, reports coarse progress, and supports cancellation without running decode or inference on the UI thread.
- A manifest-installed U2NetP session runs in-process through ONNX Runtime 1.27.1 and the `OrtValue` API. It performs metadata/name/shape/type validation, SHA-256 verification, warm-up, and same-model session reuse.
- Preprocessing follows the reviewed rembg U2NetP contract: 320×320 RGB stretch, global maximum scaling, ImageNet mean/std, and NCHW layout. First-output min/max normalization stays in float.
- The original RGB bitmap remains at original dimensions. Only the float mask is resampled during RGBA composition.
- PNG decode applies checked dimension and pixel limits before full allocation. Truncated/corrupt PNGs and unsupported formats become typed per-item errors.
- Output is encoded to a same-directory `.partial` file, flushed, and committed by rename. The active input cannot be its output.
- Production code contains no Python invocation, rembg CLI, child process, local server, HTTP client, or telemetry path.

## Changed files

81 delivery files were created. Build artifacts, the local SDK, downloaded weights, and smoke outputs are ignored.

```text
Root
  .editorconfig
  .gitignore
  CutLocal.sln
  Directory.Build.props
  Directory.Packages.props
  global.json
  README.md
  ThirdPartyNotices.txt

assets/models/manifests
  u2netp.json

docs
  architecture.md
  failure-fallback.md
  licensing.md
  memory-budget.md
  model-evaluation.md
  phase-1-plan.md
  phase-1-report.md
  provider-selection.md
  test-matrix.md

installer
  README.md

src/CutLocal.App
  App.xaml
  App.xaml.cs
  CutLocal.App.csproj
  FileDialogService.cs
  IFileDialogService.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  MainWindowViewModel.cs

src/CutLocal.Application
  CutLocal.Application.csproj
  OutputPathPolicy.cs
  RemoveBackgroundUseCase.cs

src/CutLocal.Contracts
  CutLocal.Contracts.csproj
  ProcessingContracts.cs

src/CutLocal.Domain
  CutLocal.Domain.csproj
  ModelDescriptor.cs
  ProcessingEnums.cs
  ProcessingModels.cs

src/CutLocal.Imaging
  AtomicPngWriter.cs
  BilinearAlphaCompositor.cs
  CutLocal.Imaging.csproj
  DecodedImage.cs
  FloatMaskPostprocessor.cs
  ImageServices.cs
  ImagingException.cs
  RefinedMask.cs
  SafePngDecoder.cs

src/CutLocal.Inference
  CutLocal.Inference.csproj
  IBackgroundRemovalModelAdapter.cs
  InferenceException.cs
  U2NetModelAdapter.cs
  U2NetModelAdapterFactory.cs

src/CutLocal.Infrastructure
  CutLocal.Infrastructure.csproj
  LocalBackgroundRemovalProcessor.cs
  PipelineLog.cs
  ServiceCollectionExtensions.cs

src/CutLocal.Persistence
  ApplicationPaths.cs
  CutLocal.Persistence.csproj
  JsonModelCatalog.cs
  ModelManifestValidator.cs
  ModelPathResolver.cs

tests
  Directory.Build.props
  Shared/FixtureModel.cs
  CutLocal.UnitTests/CutLocal.UnitTests.csproj
  CutLocal.UnitTests/FloatMaskPostprocessorTests.cs
  CutLocal.UnitTests/ModelManifestValidatorTests.cs
  CutLocal.UnitTests/OutputPathPolicyTests.cs
  CutLocal.UnitTests/RemoveBackgroundUseCaseTests.cs
  CutLocal.IntegrationTests/CutLocal.IntegrationTests.csproj
  CutLocal.IntegrationTests/AtomicPngWriterTests.cs
  CutLocal.IntegrationTests/CpuInferenceTests.cs
  CutLocal.IntegrationTests/FailurePathTests.cs
  CutLocal.GoldenImageTests/CutLocal.GoldenImageTests.csproj
  CutLocal.GoldenImageTests/FixtureGoldenImageTests.cs
  CutLocal.StressTests/CutLocal.StressTests.csproj
  CutLocal.StressTests/PooledMaskLifetimeTests.cs
  CutLocal.Benchmarks/CutLocal.Benchmarks.csproj
  CutLocal.Benchmarks/MaskSamplingBenchmarks.cs
  CutLocal.Benchmarks/Program.cs

tools
  CutLocal.ModelTools/CutLocal.ModelTools.csproj
  CutLocal.ModelTools/ModelSmokeRunner.cs
  CutLocal.ModelTools/Program.cs
  Install-DevelopmentModel.ps1
```

## Commands executed and observed results

The host initially had only .NET 8.0.407. The official installer placed .NET SDK 10.0.302 and runtime 10.0.10 under the ignored workspace `.dotnet` directory.

```powershell
# Download official SDK installer and rembg U2NetP release asset.
curl.exe -L ... https://dot.net/v1/dotnet-install.ps1
curl.exe -L ... https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx
Get-FileHash -Algorithm SHA256 .tools\u2netp.onnx
# Observed: 309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8

.tools\dotnet-install.ps1 -Channel 10.0 -Quality GA -InstallDir .dotnet -NoPath
# Observed: SDK 10.0.302 installed locally.

.dotnet\dotnet.exe restore CutLocal.sln
# Observed: all 14 projects restored.

.dotnet\dotnet.exe format CutLocal.sln --verify-no-changes --no-restore
# Observed: exit 0, no formatting diagnostics.

.dotnet\dotnet.exe build CutLocal.sln --no-restore --configuration Release
# Observed: succeeded, 0 warnings, 0 errors.

.dotnet\dotnet.exe test CutLocal.sln --no-build --configuration Release
# Observed: 14 passed, 0 failed, 0 skipped.
# Unit 7; integration 5; golden 1; pooled-lifetime stress 1.

.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools --configuration Release --no-build -- assets\models\manifests
# Observed: PASS u2netp 1 Apache-2.0.

.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools --configuration Release --no-build -- `
  smoke .tools\u2netp.onnx assets\models\manifests\u2netp.json .tools\u2netp-smoke-output.png
# Observed after the final mask-coordinate fix:
# PASS u2netp 1; CPU warm-up + inference + PNG in 710.3 ms.

tools\Install-DevelopmentModel.ps1
# Observed: model installed and SHA-256 verified under
# %LOCALAPPDATA%\CutLocal\models\u2netp\1\u2netp.onnx.

rg ... 'TODO|FIXME|NotImplementedException|NamedOnnxValue|Process.Start|HttpClient|MODEL_CHECKSUM_DISABLED'
# Observed: no production-code matches.
```

An explicit `dotnet list package --vulnerable --include-transitive` attempt was not executed because the environment's approval reviewer rejected disclosure of the dependency inventory to an external advisory service. Restore still treated NuGet `NU1901`–`NU1904` advisories as errors. The full authenticated vulnerability scan remains a CI gate for an authorized GitHub Actions environment.

## Test evidence

| Suite | Count | Evidence |
|---|---:|---|
| Unit | 7 | manifest/license deny rules, Unicode output naming, soft/hard/invert mask operations, non-blocking use-case boundary |
| Integration | 5 | generated ONNX CPU OrtValue inference, atomic output, corrupt PNG error, SHA-256 rejection, session reuse |
| Golden | 1 | analytic horizontal alpha gradient, MAE ≤ 6/255, IoU ≥ 0.98, bounded local regression |
| Stress | 1 | 500 repeated float-mask leases leave zero pooled owners outstanding |
| Real-model smoke | 1 manual tool run | exact SHA-pinned U2NetP metadata, warm-up, CPU inference, composition, PNG output |

The generated ONNX fixture is a small CC0 test graph that computes a channel mean. It is not substituted for the production U2NetP weight; the separate real-model smoke run proves compatibility with the actual release asset.

## Acceptance-criteria verification

| Criterion | Result |
|---|---|
| .NET 10/C# 14 solution builds with warnings as errors | Pass: Release build, 0 warnings/0 errors |
| UI does not perform decode/inference synchronously | Pass: WPF uses `AsyncRelayCommand`; use-case non-blocking test passes; pipeline offloads decode and ONNX `Run` |
| One PNG → CPU ONNX → alpha PNG | Pass with generated fixture and real U2NetP smoke |
| Output preserves original dimensions/RGB | Pass in integration test; only mask is resampled |
| Session is reused for the same model/provider/hash/path | Pass in integration test |
| Model metadata and SHA-256 are validated | Pass, including mismatch rejection before activation |
| Cancellation is observed by or immediately after current inference | Pass by cancellation checks and `RunOptions.Terminate`; no fire-and-forget operation |
| Corrupt input is isolated as a typed error | Pass for truncated PNG |
| Native/pooled owners are deterministically released | Pass by lexical disposal and 500-iteration owner test |
| Production path has no Python/process/server/network | Pass by dependency/code scan and architecture boundaries |
| Local development model is usable offline | Pass: verified per-user installation and subsequent smoke inference |

## Known risks and next-phase gates

- The exact U2NetP ONNX asset is allowed for Phase 1 development, but commercial release remains blocked until the weight-provenance record explicitly ties that artifact to the upstream Apache-2.0 grant and legal review accepts it.
- Phase 1 is CPU and PNG only. DirectML/Windows ML validation, GPU session serialization, device failure classification, and one-time CPU fallback belong to Phase 2.
- The deterministic golden fixture is not a replacement for a real U2NetP C#-versus-Python/rembg corpus comparison. That comparison and measured tolerances are Phase 2 release gates.
- Skia's cubic resize is behaviorally close but not pixel-identical to Pillow Lanczos. Quality selection cannot be made until the documented corpus measurements are complete.
- The single-item slice does not yet implement bounded channels, batch recovery, adaptive memory pressure, or hundreds-of-file soak tests; those are Phase 4/6 gates.
- Self-contained installer/portable publishing and native dependency audit are Phase 7. The current developer build uses the locally installed workspace SDK.
- The external dependency vulnerability query must run in an explicitly authorized CI environment; it was not bypassed locally after approval rejection.
