# Phase 2 executable task list

- [x] Re-check current official ONNX Runtime DirectML package/API and provider constraints.
- [x] Replace the CPU-only native package with the current DirectML package that also contains CPU EP.
- [x] Enumerate DXGI adapters, reject software/non-DirectX-12 devices, and retain stable LUID identity.
- [x] Implement deterministic Auto/DirectML/CPU candidate ordering with explicit adapter selection.
- [x] Enforce DirectML sequential execution, disabled memory patterns, graph optimization, and serialized `Run`.
- [x] Detect physical CPU cores and reserve one core for Windows/UI work.
- [x] Implement model/provider/device cache keys, leases, warm-up, two-session maximum, and idle LRU eviction.
- [x] Classify GPU OOM/device-loss failures and retry one item once on CPU after invalidating the GPU session.
- [x] Add a bounded hardware benchmark use case plus BenchmarkDotNet inference and full pipeline-stage suites.
- [x] Add provider policy, discovery, cache, and forced GPU-to-CPU fallback tests.
- [x] Run format, warnings-as-errors Release build, full tests, provider inventory, real DirectML/CPU model smokes, and an isolated BenchmarkDotNet job.

## Acceptance criteria

Phase 2 is accepted when the Phase 1 CPU path still passes, offline discovery identifies only usable DirectX 12 adapters plus CPU, DirectML options meet official constraints, the same cache key reuses a warmed session, no more than two idle/cached sessions survive LRU pressure, active leases cannot be evicted, eligible GPU failure invalidates the failed session and retries exactly once on CPU, all failures remain typed, and both real U2NetP providers plus the benchmark harness complete on the target workstation.
