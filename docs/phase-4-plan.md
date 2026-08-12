# Phase 4 implementation plan

Status: **completed and accepted** on 2026-07-24.

## Scope boundary

Phase 4 adds a durable batch engine and a virtualized desktop queue over the accepted Phase 2 processing pipeline and Phase 3 UI. The current stable decoder/output slice remains PNG-to-RGBA-PNG; additional input/output formats, model management, adaptive memory-pressure admission, and long-running leak/OOM matrices remain later phases.

## Ordered work and gates

1. Extend immutable job/item/preset state with schema versioning, attempts, provider/fallback evidence, skipped items, completed-with-errors jobs, adapter choice, and bounded concurrency.
2. Add canonical file/folder discovery with cancellation, duplicate detection, a 10,000-item cap, reparse-point avoidance, deterministic ordering, and collision-free output reservation.
3. Build `ProcessBatchUseCase` over a bounded `Channel<int>` with wait-mode backpressure, one active execution, provider-safe workers, item-boundary pause/resume, current/queued cancellation, skip/overwrite/rename behavior, failed-only retry, and per-item isolation.
4. Persist the latest immutable queue snapshot as source-generated JSON using a same-volume `.partial` file and atomic final move; validate schema, paths, identities, enums, progress, limits, and preset safety on load/save.
5. Recover a running, paused, or already interrupted job by preserving terminal items, requeueing unfinished items, marking the job interrupted, and immediately saving the normalized snapshot.
6. Add the single/batch workspace switch, multi-file and folder pickers, recursive option, queue commands, common processing settings, CPU concurrency control, per-item status/progress/time/attempt/provider/error, and open-output action.
7. Use a recycling virtualized `ListView` with lightweight rows only; keep full-resolution decode deferred until a worker admits an item.
8. Accept only after failure-continuation, cancellation, pause/resume, retry, duplicate, recovery, atomic-store, provider-concurrency, skip, ViewModel locking, XAML resource, virtualization, real render, full regression, source-invariant, and host-startup gates pass.

## Concurrency and durability decisions

- The channel capacity is 32 and `BoundedChannelFullMode.Wait` supplies explicit backpressure. The producer does not materialize image pixels; it only writes item indices.
- Requested CPU concurrency is clamped to 1–2. Auto and DirectML always execute one item at a time so the existing session/provider safety rules are not violated.
- Pause stops admission at the next item boundary. It does not pretend to suspend an in-flight native inference call. Cancellation is propagated to the in-flight pipeline and all remaining nonterminal items become cancelled in the final durable snapshot.
- Terminal item snapshots are persisted after every item. Pause, resume, recovery, queue edits, retry reset, and the final job state are also persisted.
- The store retains one recoverable last job rather than an unbounded job history.

## Acceptance result

All eight gates passed. Evidence, changed files, commands, and remaining phase boundaries are recorded in `docs/phase-4-report.md`.
