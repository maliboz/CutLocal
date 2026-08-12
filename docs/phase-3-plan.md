# Phase 3 implementation plan

Status: **completed and accepted** on 2026-07-24.

## Scope boundary

Phase 3 delivers the single-image desktop experience over the accepted Phase 2 engine. Folder discovery, a virtualized processing queue, per-item retry/remove, durable recovery, and bounded batch concurrency remain Phase 4 work.

## Ordered work and gates

1. Extend the single-image use case with explicit model, provider/adapter, mask, output-directory, suffix, and existing-file options without introducing WPF below `CutLocal.App`.
2. Isolate native dialogs, clipboard input, shell launch, bounded WPF preview decode, localization, and settings persistence behind interfaces.
3. Build the professional WPF shell: one-PNG drag/drop, file/paste input, three-column layout, settings, progress, cancellation, output access, shortcuts, and PerMonitorV2 DPI behavior.
4. Build a checkerboard before/after viewer with split control, mask mode, zoom, pan, fit, and frozen proxy images no larger than the preview budget.
5. Keep decode/inference off the dispatcher, debounce preview changes, cancel stale preview work, and atomically persist preferences.
6. Cover output safety, corrupt settings, provider/mask propagation, cancellation, invalid drops, preview bounds, clipboard ownership, XAML resource load, and real WPF rendering.
7. Accept only after format, zero-warning Release build, complete regression suite, visual render inspection, and real application-host startup smoke all pass.

## Acceptance result

All seven gates passed. Evidence and remaining boundaries are recorded in `docs/phase-3-report.md`.
