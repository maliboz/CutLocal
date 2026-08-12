# Roadmap

CutLocal's roadmap is outcome-oriented. Items are not promises and may change
after measurement or community feedback.

## 0.1 — first public release

- Publish unsigned but reproducible Windows x64 MSI and portable ZIP artifacts.
- Document checksum verification and Windows SmartScreen expectations.
- Collect real hardware and image-corpus feedback without telemetry.

## 0.2 — quality and compatibility

- Expand the licensed golden-image corpus and publish reproducible quality
  comparisons.
- Measure DirectML behavior across Intel, AMD, and NVIDIA hardware.
- Improve hair, fur, translucent edges, and difficult foreground/background
  boundaries through model and postprocessing evaluation.
- Add accessible error diagnostics and a sanitized support bundle.

## 0.3 — workflow improvements

- Evaluate more input formats after decoder and metadata security review.
- Improve batch presets, output naming, and interrupted-job recovery UX.
- Explore optional, independently licensed model packs without increasing the
  default installer footprint.

## Long-term candidates

- ARM64 only after runtime, native dependency, installer, and hardware testing.
- Microsoft Store packaging only after identity and signing requirements are
  available.
- Localization contributions beyond English and Turkish.

The privacy invariant remains unchanged: image processing stays on the user's
computer unless a future network feature is explicitly designed, opt-in, and
reviewed as a separate product capability.
