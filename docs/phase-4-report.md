# Phase 4 delivery report

Status: **accepted** on 2026-07-24 for the bounded, recoverable PNG batch workflow.

## Delivered batch workflow

- The desktop now switches between the accepted single-image workspace and a batch workspace without allowing the modes to race. An active file scan or batch locks mode switching and single-image processing.
- Batch input supports multi-select PNG files, multiple-PNG drag/drop, one folder, optional subfolder traversal, inaccessible-folder tolerance, reparse-point avoidance, deterministic ordering, canonical case-insensitive duplicate detection, and a hard 10,000-item limit.
- Output paths are reserved before execution. Unicode/full paths use the existing safe output policy; same-name destinations receive deterministic unique names. Settings may be changed before start, and noncommitted item outputs are then rebased without changing completed/skipped destinations.
- The worker engine uses a bounded wait-mode channel with capacity 32. CPU accepts at most two workers; Auto and DirectML are clamped to one. Only the admitted item is decoded, inferred, composed, and encoded.
- Pause/resume is an honest item-boundary gate. Cancellation reaches the active inference and marks every remaining nonterminal item cancelled. A failed/corrupt item becomes a typed per-item failure while siblings continue, and failed-only retry resets only failed entries while preserving attempt history.
- Existing destinations honor skip, overwrite, or rename. Skip does not enter the processor or increment the attempt count. Successful writes continue through the Phase 2 same-volume `.partial`/atomic-move PNG writer.
- Each row shows selection, filename, localized status, progress, elapsed time, attempt count, provider, and localized typed error. Queue actions cover start, pause, resume, cancel, retry failed, remove selected, clear, and open output folder.
- The list is a recycling virtualized WPF `ListView`; rows retain only strings/numbers and immutable item snapshots, not thumbnails or full-resolution bitmaps.

## Recovery and persistence

- `%LOCALAPPDATA%\CutLocal\jobs\last-job.json` stores the latest job with schema version 1. JSON metadata is source-generated; asynchronous metadata-mode serialization remains compatible with streaming APIs.
- Save writes a unique same-directory `.partial` file using asynchronous write-through I/O, flushes it, and atomically moves it over the final path. A crashed partial is ignored on recovery and cannot replace a valid snapshot.
- Load rejects empty/oversized documents, unsupported schema, more than 10,000 items, invalid enums, duplicate/empty IDs, duplicate inputs, non-full paths, unsafe preset values, and non-finite/out-of-range progress. Corrupt JSON is logged without a user path and does not crash startup.
- Running, paused, and interrupted snapshots recover as `Interrupted`; completed, failed, cancelled, and skipped items remain terminal, while unfinished items return to `Queued`. The normalized state is saved before the UI exposes it.

## UI and implementation guidance

- Microsoft guidance supports the chosen bounded-channel backpressure and single-reader/single-writer configuration: [Channels overview](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
- Microsoft WPF guidance recommends UI virtualization and recycling for large `ListView`/`ListBox` data sets; the queue explicitly enables both: [Optimizing performance: controls](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls).
- The persistence context uses `System.Text.Json` metadata source generation because it supports asynchronous serialization/deserialization while moving metadata work to compile time: [Source-generation modes](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes) and [How to use source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation).

## Changed files

Domain and contracts:

- `src/CutLocal.Domain/ProcessingEnums.cs`
- `src/CutLocal.Domain/ProcessingModels.cs`
- `src/CutLocal.Contracts/BatchContracts.cs`

Application and persistence:

- `src/CutLocal.Application/AddImagesUseCase.cs`
- `src/CutLocal.Application/AddFolderUseCase.cs`
- `src/CutLocal.Application/ProcessBatchUseCase.cs`
- `src/CutLocal.Application/ReconfigureBatchUseCase.cs`
- `src/CutLocal.Application/RecoverInterruptedJobUseCase.cs`
- `src/CutLocal.Application/RemoveBatchItemsUseCase.cs`
- `src/CutLocal.Application/RetryFailedItemsUseCase.cs`
- `src/CutLocal.Persistence/JsonProcessingJobStore.cs`
- `src/CutLocal.Persistence/PersistenceJsonContext.cs`
- `src/CutLocal.Persistence/JsonApplicationSettingsStore.cs`
- `src/CutLocal.Infrastructure/ServiceCollectionExtensions.cs`

Desktop:

- `src/CutLocal.App/App.xaml.cs`
- `src/CutLocal.App/BatchWorkspaceViewModel.cs`
- `src/CutLocal.App/FileDialogService.cs`
- `src/CutLocal.App/IFileDialogService.cs`
- `src/CutLocal.App/InverseBooleanConverter.cs`
- `src/CutLocal.App/MainWindow.xaml`
- `src/CutLocal.App/MainWindow.xaml.cs`
- `src/CutLocal.App/MainWindowViewModel.cs`
- `src/CutLocal.App/Resources/Strings.tr-TR.xaml`
- `src/CutLocal.App/Resources/Strings.en-US.xaml`

Tests and documentation:

- `tests/CutLocal.UnitTests/BatchInputAndRecoveryTests.cs`
- `tests/CutLocal.UnitTests/BatchWorkspaceViewModelTests.cs`
- `tests/CutLocal.UnitTests/JsonProcessingJobStoreTests.cs`
- `tests/CutLocal.UnitTests/ProcessBatchUseCaseTests.cs`
- `tests/CutLocal.UnitTests/Phase3TestDoubles.cs`
- `tests/CutLocal.UnitTests/MainWindowRenderTests.cs`
- `docs/phase-4-plan.md`
- `docs/phase-4-report.md`
- `docs/test-matrix.md`

## Verification

Commands completed:

```text
.\.dotnet\dotnet.exe format CutLocal.sln --no-restore --verbosity minimal
.\.dotnet\dotnet.exe build CutLocal.sln -c Release --no-restore --nologo
.\.dotnet\dotnet.exe test CutLocal.sln -c Release --no-build --no-restore --nologo
rg production-source invariant scans
controlled four-second Release CutLocal.exe startup smoke
```

- Release build: **0 warnings, 0 errors**.
- Complete regression suite: **52/52** (42 unit/UI, 8 integration, 1 golden, 1 stress), 0 skipped.
- New tests cover canonical duplicates/unique outputs, recursive discovery, recovery normalization, failed-only retry, JSON round-trip/corruption, CPU/Auto/DirectML worker clamps, pause/resume, current-and-queued cancellation, sibling continuation after one failure, skip semantics, multi-drop persistence, mode locking, and durable cancelled state.
- The STA WPF test renders both real 1380×860 workspaces. It asserts recycling virtualization and writes `phase-4-single-window.png` and `phase-4-batch-window.png` under `%TEMP%\CutLocal.Tests`; both were visually inspected after contrast, empty-overlay, and percentage-order fixes.
- The real Release executable and DI host remained healthy for four seconds in a controlled startup smoke before the test-owned process was stopped.
- Production-source scanning found no `Process.Start`, `ProcessStartInfo`, Python/rembg CLI, `HttpClient`, `HttpListener`, localhost, or `NamedOnnxValue` entry point. The only telemetry match is the existing call that explicitly disables ONNX Runtime telemetry.

## Known boundaries and next phase

- Phase 4 deliberately retains the accepted PNG input/RGBA-PNG output vertical slice. JPEG, WebP, BMP, TIFF, mask export, metadata policy, and additional backgrounds require their real decoder/encoder paths before UI exposure.
- The queue stores one recoverable last job, not job history. It does not yet offer per-row thumbnails; omitting them is the safer memory default for 10,000 items.
- Adaptive admission from live RAM pressure is not yet implemented. Provider safety clamps are deterministic; memory-pressure pausing, 500/100-item long soaks, handle/leak inspection, forced disk-full/file-lock/OOM/device-removal matrices, and long-path/Unicode stress remain Phase 6 gates.
- Final manual QA still needs screen-reader labeling, full keyboard focus order, drag/drop and folder-picker interaction, and multi-monitor DPI switching on Windows 10 and Windows 11.
- Phase 5 is next: model manager, verified manifests/download/import, optional packs, and license enforcement without creating an online requirement for inference or startup.
