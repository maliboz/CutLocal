# Changelog

All notable changes to this project will be documented in this file. The format
is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Synchronized the English and Turkish public documentation, community files,
  release workflows, and packaging guides for the `maliboz/CutLocal` repository.
- Classified the reviewed BiRefNet General Lite weight conservatively as
  non-commercial, separated its weight policy from the MIT-licensed source
  code, and prohibited that weight from every standard release package.
- Added all-platform artifact checksums, macOS release documentation, and an
  explicit pre-1.0 limitations statement.

### Fixed

- Made release-model acquisition resilient to transient HTTP, socket, timeout,
  and I/O failures while preserving HTTPS-only redirects, exact byte-length
  checks, and SHA-256 verification.

## [0.1.5] - 2026-07-25

### Fixed

- Added an inspectable macOS first-run finalizer for Gatekeeper's misleading
  “application is damaged” result on unsigned bundles produced from Windows. It
  validates the CutLocal bundle, clears only its quarantine attribute, applies an
  ad-hoc signature to every Mach-O component, verifies the result, and launches it.

## [0.1.4] - 2026-07-25

### Added

- Added a native Avalonia macOS client for the complete single-image workflow:
  local PNG selection, installed-model selection and verified download, explicit
  non-commercial license acknowledgement, CPU inference, mask controls,
  cancellation, progress, side-by-side previews, and Finder reveal.
- Added self-contained `.app` packaging for Apple Silicon and Intel, an ICNS
  application icon, `Info.plist`, release manifests, bundled-model verification,
  Windows-native dependency rejection, and TAR archives that preserve Unix modes.
- Added an original CutLocal brand mark that combines a retained foreground
  silhouette with the checkerboard convention for removed transparency.
- Added transparent master/512 px PNG assets, a nine-size Windows ICO, and a
  concise brand usage guide under `assets/branding`.

### Changed

- Retargeted the domain/application/inference/imaging/persistence core to portable
  .NET 10 while retaining the WPF Windows shell and DirectML acceleration.
- Isolated ONNX native runtime ownership per application: DirectML 1.24.4 on
  Windows, CPU 1.24.4 on Apple Silicon, and the last official Intel-capable CPU
  line (1.23.2) as a complete managed/native unit on macOS x64.
- Replaced the placeholder `C` badge and empty-preview glyph with the CutLocal
  mark, added branded window icons to the main and model-manager windows, and
  embedded the multi-resolution icon into the executable and release shortcuts.
- Added the project mark to both English and Turkish README headers.

## [0.1.3] - 2026-07-25

### Fixed

- Corrected before/after comparison clipping so the original and processed
  previews occupy separate sides of the divider. Transparent pixels in the
  processed PNG now reveal the checkerboard instead of the original background.
- Kept the comparison divider disabled unless both preview sources are present,
  and added a WPF render regression assertion for non-overlapping clips.

## [0.1.2] - 2026-07-25

### Added

- Added BRIA RMBG-2.0 as an optional, user-initiated CPU model for explicitly
  acknowledged non-commercial use under CC BY-NC 4.0. Its weights are never
  bundled in CutLocal release packages.
- Added bilingual guidance for threshold, feather radius, and restricted-model
  licensing in the desktop UI and documentation.

### Changed

- Threshold now recenters soft alpha mattes while preserving `0.50` as the
  neutral value; hard-cut mode remains a strict binary threshold.
- Feather radius now applies a scaled separable Gaussian refinement expressed
  in original-output pixels, so the setting behaves consistently across image
  and model resolutions.

### Fixed

- Kept restricted reviewed models catalog-visible while requiring an explicit
  acknowledgement before any download or repair network request.
- Added release validation that rejects every non-commercial model weight from
  MSI and portable payloads.

## [0.1.1] - 2026-07-25

### Fixed

- Prevented model-manager and main-window shutdown from closing a WPF window
  reentrantly during collection-change dispatch, which previously crashed the
  application when opening or closing model management.

## [0.1.0] - 2026-07-25

### Added

- Local single-image and bounded batch background removal for PNG files.
- Original-resolution transparent PNG output with adjustable mask controls.
- Self-contained in-process ONNX Runtime inference with CPU and DirectML policy,
  GPU failure fallback, and warmed session caching.
- Bundled, hash-verified U2NetP fast model and optional BiRefNet General Lite
  model management.
- Crash recovery, atomic outputs, long/Unicode path handling, cancellation,
  retry, and bounded memory/resource behavior.
- English and Turkish WPF resources, keyboard operation, high-DPI behavior, and
  before/after preview.
- Validated portable ZIP and per-user WiX MSI release pipelines.
- Unit, integration, golden-image, stress, packaging, license, and dependency
  audit gates.
- Apache-2.0 project license, upstream NOTICE, third-party inventory, bilingual
  README, and complete open-source community/security templates.

### Fixed

- Removed a WPF startup deadlock caused by synchronously waiting for asynchronous
  bundled-model activation on the dispatcher thread. Startup now awaits host and
  model initialization without blocking UI message processing.
- Prevented a late asynchronous progress callback from overwriting the completed
  single-image progress and status.
- Added deterministic MSI directory cleanup rows and kept all Windows Installer
  validation enabled except ICE91, which is inapplicable to the fixed per-user
  package scope.

[Unreleased]: https://github.com/maliboz/CutLocal/compare/v0.1.5...HEAD
[0.1.5]: https://github.com/maliboz/CutLocal/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/maliboz/CutLocal/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/maliboz/CutLocal/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/maliboz/CutLocal/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/maliboz/CutLocal/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/maliboz/CutLocal/releases/tag/v0.1.0
