# Contributing to CutLocal

Thank you for helping improve a privacy-first desktop application. Contributions
of code, tests, documentation, translations, model research, and reproducible bug
reports are welcome.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
Security vulnerabilities must be reported privately as described in
[SECURITY.md](SECURITY.md).

## Before opening work

- Search existing issues and pull requests first.
- Use an issue for behavior changes that need design discussion.
- Keep each pull request focused on one coherent change.
- Do not add model weights, generated release files, personal paths, credentials,
  certificates, or secrets to Git history.
- Do not add telemetry, image uploads, or a runtime network dependency without an
  explicit architecture decision and maintainer approval.

## Development environment

Requirements:

- Windows 10 or 11 x64 for the WPF application and Windows packaging
- macOS 14+ for native Mac launch and UI validation
- .NET 10 SDK selected by `global.json`
- PowerShell 7 is recommended for release scripts

```powershell
dotnet restore CutLocal.sln
dotnet build CutLocal.sln --configuration Release --no-restore
dotnet test CutLocal.sln --configuration Release --no-build --no-restore
```

Run the desktop application:

```powershell
dotnet run --project src\CutLocal.App\CutLocal.App.csproj --configuration Release
```

Installing the pinned development model is optional:

```powershell
.\tools\Install-DevelopmentModel.ps1
```

## Engineering expectations

- Preserve the dependency boundaries documented in
  [docs/architecture.md](docs/architecture.md).
- Keep nullable analysis, analyzers, warnings-as-errors, deterministic builds, and
  central package versions enabled.
- Keep file, stream, bitmap, tensor, and ONNX Runtime ownership explicit and
  deterministic.
- Keep UI work asynchronous and cancellable where the platform allows it.
- Keep inference local. Log events and safe metadata, never image contents or full
  user paths.
- Add or update tests for every observable behavior change.
- Update English and Turkish resources together when changing user-facing text.
- Run `dotnet format CutLocal.sln` before submitting.

## Model and dependency changes

A model manifest must include a stable identifier, exact version, HTTPS source,
exact file length, SHA-256, SPDX expression or a project-defined `LicenseRef`,
provenance, input/output contract, and provider support. Unknown or restricted
licensing must fail closed in standard release packaging. Never commit model
weights such as `.onnx`, `.pth`, or `.safetensors` files.

For any production dependency or model change:

```powershell
dotnet run --project tools\CutLocal.ModelTools\CutLocal.ModelTools.csproj `
  --configuration Release -- assets\models\manifests --noncommercial
.\installer\Validate-LicenseInventory.ps1
```

Update `ThirdPartyNotices.txt`, the relevant license snapshot, and documentation
in the same pull request.

## Pull request checklist

Your pull request should:

- explain the problem and the chosen solution;
- link the related issue when one exists;
- include verification commands and results;
- include screenshots for visible UI changes;
- call out privacy, memory, performance, compatibility, and licensing impact;
- avoid unrelated formatting or generated files; and
- update `CHANGELOG.md` for user-visible changes.

Contributions are submitted under the repository's Apache-2.0 license unless
explicitly marked otherwise before submission. Apache-2.0 section 5 describes
the contribution terms.
