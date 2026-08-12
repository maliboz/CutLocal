# Phase 1 executable task list

- [x] Create the required solution/layer/test/tool folders with strict build settings.
- [x] Define pure domain records, typed outcomes/errors, manifest schema, and boundary interfaces.
- [x] Add a fail-closed manifest validator and local model path resolver.
- [x] Implement safe PNG metadata/decode with dimension and decompression-bomb limits.
- [x] Implement U2NetP preprocessing compatible with the reviewed rembg behavior.
- [x] Create one reusable CPU `InferenceSession` using `OrtValue`, metadata checks, warm-up, and deterministic disposal.
- [x] Keep mask calculations in float, compose alpha at original dimensions, and atomically encode PNG.
- [x] Add a minimal WPF/MVVM file-select → process → result vertical slice with cancellation and no UI-thread inference.
- [x] Add manifest/output unit tests, generated-fixture CPU integration tests, and a golden alpha test.
- [x] Add a manifest/license validation CLI and weight-free U2NetP manifest.
- [x] Run .NET 10 restore/build/tests on this workstation and record exact results in `docs/phase-1-report.md`.

## Acceptance criteria

Phase 1 is accepted when the solution restores and builds with warnings as errors, tests pass on `win-x64`, a PNG can be processed with the installed hash-verified U2NetP model while the WPF dispatcher remains responsive, output is an atomically written alpha PNG at original dimensions, cancellation is observed no later than completion of the current inference, and no Python/process/server/network call exists in the production path.
