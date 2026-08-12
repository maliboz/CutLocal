# Phase 5 — Model manager report

> Historical implementation record. Current model distribution policy in
> `docs/licensing.md` supersedes the license assumptions recorded during this
> phase. In particular, the reviewed BiRefNet weight is not classified as MIT.

## Outcome

Phase 5 is complete. CutLocal now has an offline-first, manifest-driven Model Manager with exact package length, SHA-256 activation gates, HTTPS-only manual redirects, safe range resume, pause, quarantine, repair, delete, and acknowledged local ONNX import.

The first real optional pack is BiRefNet General Lite. Its complete official asset was independently downloaded twice and produced the same exact size and SHA-256. A production-adapter CPU smoke test loaded the graph, validated its tensor metadata, warmed the session, ran inference, postprocessed the sigmoid output, and wrote an original-size PNG.

## Delivered behavior

- Model Manager lists installed and available models with size, version, SPDX license/source, provider compatibility, input size, state, and progress.
- Catalog inspection performs no network access.
- Only explicit Download or Repair commands can reach the network.
- Downloads stream directly to `.partial`; the 224 MB package is never buffered as one managed byte array.
- Resume requires a valid `206`/`Content-Range`; ignored Range restarts without appending; a valid complete-partial `416` path proceeds to hash verification.
- Every redirect hop must remain HTTPS and the redirect count is bounded.
- Final activation requires exact byte length and SHA-256. Mismatch/invalid response content is moved to a controlled quarantine directory.
- Pause cancels the transfer while retaining the partial file. Resume reuses it.
- Repair invalidates cached sessions, quarantines a bad final file, and downloads again.
- Delete invalidates cached sessions and removes controlled final/partial content. Accepted custom manifests/receipts are removed with their custom package.
- Custom ONNX import requires an explicit companion manifest and license acknowledgement; size, hash, tensor names/types/ranks/dimensions, resize mode, activation, and providers are validated before atomic installation.
- Noncommercial/BRIA custom manifests are catalog-visible only after the acceptance receipt exists; they are still blocked from commercial built-in downloads/default selection.
- BiRefNet uses the manifest-defined `sigmoid-minmax` output policy; U2NetP retains `minmax`.
- UI strings are available in Turkish and English and the model list uses recycling virtualization.

## BiRefNet General Lite verification

| Check | Result |
|---|---|
| Official asset size | `224005088` bytes |
| SHA-256 | `5600024376F572A557870A5EB0AFB1E5961636BEF4E1E22132025467D0F03333` |
| MD5 cross-check | `4FAB47ADC4FF364BE1713E97B7E66334` |
| License | MIT |
| Tensor input | float NCHW `1×3×1024×1024` |
| Production-adapter provider | CPU |
| Warm-up + measured inference | PASS; measured inference about `8826.8 ms` on this machine |
| Output | original synthetic input size `96×64`; only the mask is rescaled |

The MD5 cross-check matches the value in the [rembg BiRefNet General Lite session](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/birefnet_general_lite.py). The reviewed upstream [BiRefNet source repository](https://github.com/ZhengPeng7/BiRefNet) is [MIT-licensed](https://github.com/ZhengPeng7/BiRefNet/blob/main/LICENSE), but that source-code license does not establish a permissive license for the separately distributed weight. Current policy classifies the weight as `LicenseRef-BiRefNet-Weights-NonCommercial`.

## Acceptance evidence

- Release build: success, `0` warnings, `0` errors.
- Full solution tests: `69/69` passed.
  - Unit: `59/59`
  - Integration: `8/8`
  - Golden image: `1/1`
  - Stress: `1/1`
- Historical model manifest result: BiRefNet was accepted under the former
  assumption. Current validation requires
  `LicenseRef-BiRefNet-Weights-NonCommercial` and forbids bundling it; U2NetP
  remains accepted under Apache-2.0.
- Formatter: `dotnet format --verify-no-changes` passed after applying the repository's CRLF/import/indent rules.
- Real BiRefNet CPU smoke: passed with no fallback.
- Source security scan:
  - `HttpClient.SendAsync` exists only in `ModelPackageManager`;
  - inference contains no download/network path;
  - no `.onnx` file exists under `assets/`;
  - ONNX Runtime telemetry remains explicitly disabled;
  - no `NamedOnnxValue`, local HTTP server, or Python/child-process inference path was introduced.
- Hidden Release host smoke stayed alive through startup for four seconds. Because `-WindowStyle Hidden` exposes no main-window handle, the diagnostic process required forced termination; graceful `Window.Close()` and `Application.Shutdown()` are covered by the passing STA WPF render test.

## Render and smoke artifacts

- Model Manager: `%TEMP%\CutLocal.Tests\phase-5-model-manager.png`
- BiRefNet synthetic smoke output: `C:\tmp\CutLocal.Phase5\birefnet-smoke.png`

The temporary 224 MB ONNX test copy was removed after the smoke run. It was never copied into the repository or application model store.

## Commands executed

```powershell
.\.dotnet\dotnet.exe build CutLocal.sln -c Release --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.UnitTests\CutLocal.UnitTests.csproj -c Release --no-restore
.\.dotnet\dotnet.exe test CutLocal.sln -c Release --no-restore
.\.dotnet\dotnet.exe format CutLocal.sln --no-restore
.\.dotnet\dotnet.exe format CutLocal.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools\CutLocal.ModelTools.csproj -c Release --no-build -- assets\models\manifests
.\.dotnet\dotnet.exe run --project tools\CutLocal.ModelTools\CutLocal.ModelTools.csproj -c Release --no-build -- smoke <temporary-birefnet.onnx> assets\models\manifests\birefnet-general-lite.json <temporary-output.png> --provider cpu --iterations 1
```

Additional PowerShell checks verified the official asset size/hash, the controlled temporary deletion target, source network boundaries, absence of committed model weights, and hidden-host startup health.

## Changed files

### Catalog and notices

- `assets/models/manifests/u2netp.json`
- `assets/models/manifests/birefnet-general-lite.json`
- `ThirdPartyNotices.txt`

### Domain, contracts, application

- `src/CutLocal.Domain/ModelDescriptor.cs`
- `src/CutLocal.Contracts/ProcessingContracts.cs`
- `src/CutLocal.Application/ModelManagementUseCase.cs`

### Persistence, inference, imaging, infrastructure

- `src/CutLocal.Persistence/ApplicationPaths.cs`
- `src/CutLocal.Persistence/JsonModelCatalog.cs`
- `src/CutLocal.Persistence/ModelManifestValidator.cs`
- `src/CutLocal.Imaging/FloatMaskPostprocessor.cs`
- `src/CutLocal.Inference/IModelAdapterSessionCache.cs`
- `src/CutLocal.Inference/U2NetModelAdapter.cs`
- `src/CutLocal.Inference/U2NetModelAdapterFactory.cs`
- `src/CutLocal.Inference/OnnxModelCompatibilityValidator.cs`
- `src/CutLocal.Infrastructure/ModelPackageManager.cs`
- `src/CutLocal.Infrastructure/ServiceCollectionExtensions.cs`

### WPF/MVVM

- `src/CutLocal.App/App.xaml.cs`
- `src/CutLocal.App/IFileDialogService.cs`
- `src/CutLocal.App/FileDialogService.cs`
- `src/CutLocal.App/IModelManagerDialog.cs`
- `src/CutLocal.App/ModelManagerViewModel.cs`
- `src/CutLocal.App/ModelManagerWindow.xaml`
- `src/CutLocal.App/ModelManagerWindow.xaml.cs`
- `src/CutLocal.App/MainWindow.xaml`
- `src/CutLocal.App/MainWindowViewModel.cs`
- `src/CutLocal.App/Resources/Strings.tr-TR.xaml`
- `src/CutLocal.App/Resources/Strings.en-US.xaml`

### Tests and documentation

- `tests/CutLocal.UnitTests/FloatMaskPostprocessorTests.cs`
- `tests/CutLocal.UnitTests/ModelManifestValidatorTests.cs`
- `tests/CutLocal.UnitTests/ModelPackageManagerTests.cs`
- `tests/CutLocal.UnitTests/JsonModelCatalogTests.cs`
- `tests/CutLocal.UnitTests/MainWindowRenderTests.cs`
- `docs/phase-5-plan.md`
- `docs/phase-5-report.md`

## Known risks and deliberate limits

- BiRefNet Lite is CPU-only in the reviewed manifest for this phase. DirectML is not advertised until Phase 6 measures memory pressure, device loss, and output tolerance on representative GPUs.
- Quality BiRefNet General and a portrait pack are not added without the same exact provenance/license/hash/size and real-adapter validation.
- Custom import deliberately requires a companion manifest; arbitrary ONNX preprocessing is not guessed.
- Manager-open hash inspection reads installed model files from disk and can take noticeable time for large packages, but it is asynchronous and performs no network access.
- Standard-installer default-model bundling and upgrade behavior remain Phase 7 work.

## Next phase

Phase 6 should harden long-running behavior: repeated model switching, partial/quarantine accumulation, handle/memory leak inspection, corrupt ONNX fuzz cases, disk-full simulation, long/Unicode paths, large-image pressure, DirectML failure/OOM, and measured CPU/GPU concurrency.
