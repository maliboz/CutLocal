# Phase 7 — release plan

> Historical plan. Current 0.1.5 packaging and license policy in
> `docs/release.md` and `docs/licensing.md` supersede optional balanced-model
> bundling described below.

## Goal

Produce self-contained Windows x64 installer and portable distributions that open offline with a trusted default model, carry complete notices, support upgrade/uninstall, and can be signed safely in CI without committing certificate material.

## Decisions

1. Keep a multi-file self-contained layout. WPF, ONNX Runtime, DirectML, and Skia native probing remain explicit; single-file extraction, trimming, and Native AOT stay disabled.
2. Enable ReadyToRun. Local measurements showed a small package-size cost with better first/median startup than IL-only publishing.
3. Use separate `Portable` and `Installer` publish profiles even while their current runtime settings match.
4. Pin WiX Toolset 5.0.2 under MS-RL. Current Inno Setup and WiX 6+ introduce commercial licensing/maintenance conditions that should not be imposed silently on the product owner.
5. Use a per-user MSI at `%LOCALAPPDATA%\Programs\CutLocal`, fixed UpgradeCode, Windows Installer uninstall/repair, Start Menu shortcut, optional desktop feature, and optional staged model feature.
6. Download release model weights only into ignored build artifacts and require exact HTTPS, byte length, and SHA-256 validation before staging.
7. Store a hash/length manifest for every staged file, validate the portable ZIP entry-by-entry, validate MSI tables through Windows Installer, and emit distributable SHA-256 sums.
8. Sign by certificate-store thumbprint with SHA-256 and RFC 3161 timestamps. CI imports a secret PFX only for the job and removes it in an `always()` cleanup step.

## Acceptance criteria

- Release build emits a portable ZIP and per-user MSI for `win-x64`.
- Both packages are self-contained and include the default U2NetP model, native dependencies, notices, and canonical license texts.
- No PDB, `.partial`, `.seeding`, committed model weight, PFX, password, or private key enters the distribution/source tree.
- Portable archive paths are safe and every manifest hash/length matches.
- MSI contains Start Menu and optional Desktop shortcuts, fixed major-upgrade metadata, an embedded cabinet, required fast-model feature, and optional balanced-model authoring.
- Clean install, offline launch, file-bearing major upgrade, optional desktop install, and uninstall all pass on Windows.
- Uninstall removes program files and shortcuts while preserving `%LOCALAPPDATA%\CutLocal` user data.
- CI covers restore, formatting, build, all test assemblies, manifest/license policy, NuGet vulnerability audit, packaging, and artifact upload.
- Release CI supports optional signing and provenance attestation without exposing secrets.
- Known legal boundary is explicit: the owner supplies the product-specific EULA/publisher certificate before public commercial distribution.
