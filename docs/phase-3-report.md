# Phase 3 delivery report

Status: **accepted** on 2026-07-24 for the single-image desktop UI.

## Delivered experience

- The main WPF window is a responsive three-column desktop surface: input/drop card, before/after workspace, and scrollable settings. A compact footer shows progress, localized status, output path, provider, elapsed time, and the explicit open-folder action.
- Input supports the native PNG picker, exactly-one-PNG drag/drop, and clipboard file/image capture. Temporary clipboard PNGs are written under the controlled application data root and deleted only when CutLocal owns the path.
- The comparison viewer uses a checkerboard transparency surface, draggable split slider, before/after labels, mask-only toggle, 10–400% zoom, Ctrl+wheel zoom, mouse pan, and fit-to-screen.
- Preview images are decoded away from the dispatcher, frozen before crossing threads, and capped to a 1,600-pixel longest edge by the ViewModel (the service rejects requests above 2,048). Full-resolution input still flows through the Phase 2 processing pipeline, so preview memory does not reduce output dimensions.
- Settings expose manifest-backed model choice, Auto/concrete DirectML adapter/CPU choice, output directory, safe filename suffix, skip/overwrite/rename behavior, fixed RGBA PNG and preserved dimensions, threshold, feather, hard cut, invert mask, and live preview.
- A 450 ms live-preview debounce coalesces mask changes and cancels stale work. Processing reports coarse localized phases and supports explicit cancellation without decoding or inference on the WPF dispatcher.
- Turkish and English use replaceable dynamic resource dictionaries. `Ctrl+O`, `Ctrl+V`, `Ctrl+Enter`, and `Esc` cover the primary keyboard flow; the application manifest declares `PerMonitorV2` DPI awareness and long-path awareness.
- Language, model, provider/adapter, output, suffix, existing-file behavior, mask settings, and live-preview preference are saved to `%LOCALAPPDATA%\CutLocal\settings.json`. The JSON is capped at 64 KiB, parsed defensively, atomically replaced, debounced during use, and flushed before shutdown.

## Architecture and safety

- WPF types remain in `CutLocal.App`; Domain, Contracts, Application, Inference, Imaging, Infrastructure, and Persistence keep their prior dependency boundaries.
- `MainWindowViewModel` owns commands and state. Code-behind contains only window lifetime and native drag/drop adaptation. File dialogs, clipboard, preview decode, shell launch, localization, and settings are injected interfaces.
- Output naming canonicalizes the input and destination, rejects traversal/path separators and invalid suffix characters, and continues to rely on the atomic Phase 2 PNG writer.
- Opening the output directory uses `SHParseDisplayName` and `SHOpenFolderAndSelectItems`; it does not call `Process.Start` or create a helper process.
- Diagnostics do not log input/output paths or image content. Inference remains in-process, offline, and free of Python, local HTTP, telemetry, or component installation.
- WPF implementation choices follow Microsoft guidance to keep dispatcher work small and to use native drag/drop and scrolling primitives: [WPF threading model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model), [drag-and-drop overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview), and [ScrollViewer](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/scrollviewer).

## Verification

Commands completed:

```text
.\.dotnet\dotnet.exe restore CutLocal.sln --locked-mode
.\.dotnet\dotnet.exe format CutLocal.sln --no-restore --verbosity minimal
.\.dotnet\dotnet.exe build CutLocal.sln -c Release --no-restore
.\.dotnet\dotnet.exe test CutLocal.sln -c Release --no-build --no-restore
```

- Release build: **0 warnings, 0 errors**.
- Complete regression suite: **37/37** (27 unit/UI, 8 integration, 1 golden, 1 stress), 0 skipped.
- The STA render test loads real application resources, opens the actual `MainWindow`, renders a 1380×860 software bitmap, and asserts a non-empty PNG artifact. Visual inspection was repeated after theme fixes.
- The render test found and drove a fix for an initialization-order null reference in the zoom control; the regression now passes.
- The real Release executable and DI host remained healthy during a controlled four-second startup smoke, then the test-owned process was stopped.
- A final production-source invariant scan found no `Process.Start`, `ProcessStartInfo`, Python, `HttpClient`, `HttpListener`, localhost, or telemetry entry points.

## Phase boundary and remaining validation

- Folder selection, processing mode, queue rows, retry/remove, per-file timing/status, virtualization, recovery, pause/resume, and concurrency controls belong to Phase 4 and are intentionally absent from the Phase 3 single-image window.
- Auto crop, crop margin, custom/white background, advanced erode/dilate/island/hole/color-decontamination controls, 16-bit PNG, and tiled inference require corresponding engine behavior before UI controls may expose them.
- Automated tests cover clipboard ownership cleanup but do not mutate the user's real Windows clipboard. Final release QA still needs manual keyboard focus order, screen-reader labeling, clipboard paste, drag/drop, and multi-monitor DPI switching on Windows 10 and Windows 11.
- Long UI soaks, handle-leak inspection, corrupt-image matrices, forced native OOM/device removal, and 4K/8K memory pressure remain Phase 6 gates.
