# CutLocal architecture and technical decision record

Status: accepted through Phase 3 on 2026-07-24. Later phases may amend a decision through a new ADR; they must not silently reverse it.

## Decision summary

| ID | Decision | Why |
|---|---|---|
| ADR-001 | WPF on self-contained .NET 10, C# 14, `win-x64` | Meets Windows 10/11, accessibility, DPI, and no-runtime-install goals. |
| ADR-002 | ONNX Runtime runs in-process through the `OrtValue` API | Avoids Python, child processes, HTTP, and the allocation-heavy legacy `NamedOnnxValue` API. |
| ADR-003 | CPU is the Phase 1 baseline and always-available fallback | It is deterministic to deploy and does not depend on a GPU driver/provider. |
| ADR-004 | Model behavior is manifest-driven, not an enum | New models can declare shape, layout, normalization, activation, compatibility, hash, provenance, and license without changing domain types. |
| ADR-005 | Inference never downloads | Downloading belongs to the later Model Manager. Activation requires an existing local file whose SHA-256 matches the manifest. |
| ADR-006 | SkiaSharp owns decode/encode/composition; disposable native objects have lexical ownership | It supports the required formats and avoids WPF bitmap retention in the processing path. |
| ADR-007 | Long operations are asynchronous and cancellable at item boundaries | ONNX `Run` is synchronous; Phase 1 runs it off the UI thread and observes cancellation before and immediately after the current inference. |
| ADR-008 | Outputs and JSON state use same-volume temporary files followed by atomic rename | A crash cannot expose a partially encoded final output or half-written state file. |
| ADR-009 | Domain and contracts contain no WPF, file-system, Skia, or ONNX types | Keeps policy independently testable and prevents UI/inference coupling. |
| ADR-010 | No telemetry and no network dependency in the execution path | Privacy is a product invariant, not a preference. |
| ADR-011 | Bundle `Microsoft.ML.OnnxRuntime.DirectML` and use its included CPU EP | One native ORT build supplies the Windows GPU path and the always-available CPU fallback without conflicting native ORT packages. |
| ADR-012 | Discover DirectML devices through DXGI and verify DirectX 12 capability | DirectML device IDs follow DXGI enumeration, while stable LUID identity and dedicated memory prevent assuming adapter 0 is fastest. |
| ADR-013 | Cache at most two warmed model/provider/device sessions behind leases | Lease counts prevent eviction during use; idle LRU eviction and invalidation give deterministic native disposal. |
| ADR-014 | Retry an eligible GPU runtime failure once on CPU | Device loss and GPU OOM do not terminate the item or create an infinite retry loop. |
| ADR-015 | WPF renders frozen preview proxies capped at 1,600 pixels while the processing path retains original dimensions | Large source images do not remain as full-resolution WPF bitmaps and UI preview memory is independent from output quality. |
| ADR-016 | The ViewModel owns commands/state; code-behind is limited to WPF lifetime and native drag/drop events | File dialogs, clipboard, preview decoding, shell launch, localization, and settings remain replaceable/testable service boundaries. |
| ADR-017 | Non-secret preferences use a bounded, atomically replaced local JSON document | A corrupt or partial settings write cannot prevent startup; unsupported values fall back to safe UI defaults. |
| ADR-018 | The desktop manifest declares PerMonitorV2 DPI awareness and the UI uses dynamic Turkish/English resources plus explicit shortcuts | Scaling, localization, and keyboard operation are product behavior rather than implicit platform defaults. |

The recommended `OrtValue` API and prompt disposal of `OrtValue`, result collections, sessions, options, and pinned buffers follow the [official ONNX Runtime C# guidance](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html). Rembg is a behavioral reference, not a runtime dependency. Its current base normalization and U2NetP postprocessing were reviewed directly in [`base.py`](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/base.py) and [`u2netp.py`](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/u2netp.py).

## Dependency direction

```text
CutLocal.App
  -> CutLocal.Infrastructure
  -> CutLocal.Application -> CutLocal.Contracts -> CutLocal.Domain
CutLocal.Infrastructure
  -> CutLocal.Inference -> CutLocal.Imaging -> CutLocal.Contracts
  -> CutLocal.Persistence -> CutLocal.Contracts
```

No lower layer references `CutLocal.App`. `CutLocal.Domain` has no project references.

## Solution tree

```text
CutLocal.sln
src/
  CutLocal.App/              WPF composition root, MVVM desktop surface, and preview controls
  CutLocal.Application/      use cases and orchestration
  CutLocal.Domain/           pure domain records and state enums
  CutLocal.Infrastructure/   cross-layer local processing pipeline and DI
  CutLocal.Inference/        provider discovery, session cache, and OrtValue adapters
  CutLocal.Imaging/          safe PNG decode, mask composition, atomic encode
  CutLocal.Persistence/      manifest catalog and local paths
  CutLocal.Contracts/        layer-boundary requests and service contracts
tests/
  CutLocal.UnitTests/
  CutLocal.IntegrationTests/
  CutLocal.GoldenImageTests/
  CutLocal.StressTests/
  CutLocal.Benchmarks/
tools/
  CutLocal.ModelTools/       build-time manifest/license validator
installer/                   Phase 7 packaging inputs
docs/
assets/
  models/manifests/          weight-free model metadata
```

## Domain and service boundaries

The domain owns `ProcessingJob`, `ProcessingItem`, `ModelDescriptor`, `InferenceProviderDescriptor`, `ProcessingPreset`, `OutputConfiguration`, `ApplicationSettings`, status enums, typed `ProcessingError`, and `BenchmarkResult`.

The Phase 3 single-image call chain is:

```text
MainWindowViewModel
  -> IFileDialogService + IClipboardService
  -> IPreviewBitmapService + ILocalizationService
  -> IApplicationSettingsStore + IFileLauncher
  -> RemoveBackgroundUseCase
    -> IRemoveBackgroundProcessor
      -> IModelCatalog + IModelPathResolver
      -> IInferenceProviderCatalog + ProviderSelectionService
      -> IModelAdapterSessionCache
      -> IImageDecoder
      -> IBackgroundRemovalModelAdapter
      -> IMaskCompositor
      -> IAtomicImageWriter
```

`IBackgroundRemovalModelAdapter` separates preprocessing, execution, and postprocessing. It receives a caller-owned pooled tensor buffer; returned masks own their pooled buffers and are deterministically disposed. Input/output node names are discovered from session metadata and checked against optional manifest names. The session cache key contains model id, version, hash, canonical path, provider, stable device id, and current DXGI index. Callers hold `IModelAdapterLease` while using a session, so LRU eviction cannot dispose an active native object.

DirectML sessions enforce sequential execution, disabled memory patterns, graph optimization level `ORT_ENABLE_ALL`, and one `Run` at a time. CPU sessions reserve one detected physical core for Windows/UI work. Auto currently orders offline-ready DirectML devices by dedicated video memory and then CPU; it never calls a component-install or provider-download API.

Preview decode and alpha-proxy generation run away from the dispatcher, return frozen `BitmapSource` objects, and never replace the original-resolution processing input. The `BeforeAfterViewer` performs comparison with clipping over a checkerboard surface and owns fit, zoom, pan, split, and mask-only display state. Processing and stale live-preview work share explicit cancellation; mask-control changes use a 450 ms debounce. Settings use a separate 650 ms debounce and are flushed before window shutdown.

The planned use-case surface for later phases is `AddImages`, `AddFolder`, `ProcessBatch`, `CancelJob`, `RetryFailedItems`, `ExportResult`, `ModelManagement`, `BenchmarkHardware`, and `RecoverInterruptedJob`. Interfaces are introduced only when a phase supplies a real implementation; this avoids speculative placeholder implementations.

## U2NetP Phase 1 behavioral contract

1. Decode only the requested file when work begins and reject dimensions/pixel counts before allocating the full bitmap.
2. Convert to RGB and resize to 320×320.
3. Divide channels by the maximum RGB byte present (with a non-zero guard), then apply ImageNet mean `[0.485, 0.456, 0.406]` and standard deviation `[0.229, 0.224, 0.225]` in NCHW order.
4. Run the first model output through min/max normalization, preserving float precision.
5. Resample only the mask to original dimensions while composing RGBA; never downscale and re-upscale the original RGB image.
6. Encode to a temporary PNG, flush it, and atomically move it to the final path.

Pillow Lanczos and Skia sampling are not assumed pixel-identical. The golden
tolerance is documented in `docs/test-matrix.md`; a real upstream reference
corpus comparison remains a quality gate before changing the default model.

## Security and privacy invariants

- Inference, decode, composition, and encode have no HTTP client dependency.
- ONNX paths resolve under the application model root and the model file must match a pinned SHA-256.
- HTTPS is mandatory in manifests even though Phase 1 never downloads.
- Input content and full paths are not logged. Paths are displayed only in the local UI and locally persisted only when the user selects an output directory.
- Native objects are loaded from NuGet/self-contained publish assets and model data is never treated as executable plugin code.
