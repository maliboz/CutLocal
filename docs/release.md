# CutLocal release guide

## Release contents

Version 0.1.5 produces:

- `CutLocal-0.1.5-win-x64-portable.zip`
- `CutLocal-0.1.5-win-x64-setup.msi`
- `CutLocal-0.1.5-macos-arm64.tar.gz`
- `CutLocal-0.1.5-macos-x64.tar.gz`
- `SHA256SUMS.txt`

Windows packages are self-contained .NET 10 ReadyToRun distributions. macOS
packages are architecture-specific self-contained Avalonia applications. End
users do not install Python or a separate .NET runtime. U2NetP is bundled and
hash-verified so the default inference path starts offline.

Standard packages must not contain BiRefNet General Lite, BRIA RMBG-2.0, or any
other manifest with `commercialUseAllowed: false`. Restricted models remain
user-initiated downloads behind an explicit acknowledgement and exact integrity
verification.

## Build prerequisites

- Windows x64
- the .NET 10 SDK selected by `global.json`
- network access during NuGet restore and reviewed U2NetP acquisition
- optional Windows SDK SignTool and a certificate for Authenticode signing
- a native Mac for final Developer ID signing, notarization, and launch validation

No separately installed WiX application is required.

```powershell
dotnet restore CutLocal.sln
dotnet restore src\CutLocal.App\CutLocal.App.csproj --runtime win-x64 -p:PublishReadyToRun=true
dotnet restore installer\CutLocal.Setup.wixproj
.\installer\Build-Release.ps1 -Version 0.1.5
.\installer\Build-MacRelease.ps1 -Version 0.1.5
```

## Integrity verification

```powershell
Get-Content artifacts\release\SHA256SUMS.txt
Get-FileHash artifacts\release\CutLocal-0.1.5-win-x64-portable.zip -Algorithm SHA256
Get-FileHash artifacts\release\CutLocal-0.1.5-win-x64-setup.msi -Algorithm SHA256
.\installer\Validate-PortableArchive.ps1 artifacts\release\CutLocal-0.1.5-win-x64-portable.zip 0.1.5
.\installer\Validate-Installer.ps1 artifacts\release\CutLocal-0.1.5-win-x64-setup.msi 0.1.5
```

The portable validator rejects absolute or traversal paths, duplicates, symbols,
and temporary files, then streams every payload through SHA-256. The MSI
validator reads the Windows Installer database without installation.

## Installer behavior

The application and U2NetP fast model are required. The Desktop shortcut is
optional. Silent examples:

```powershell
msiexec.exe /i CutLocal-0.1.5-win-x64-setup.msi /qn /norestart
msiexec.exe /i CutLocal-0.1.5-win-x64-setup.msi ADDLOCAL=ApplicationFeature,FastModelFeature,DesktopShortcutFeature /qn /norestart
msiexec.exe /x CutLocal-0.1.5-win-x64-setup.msi /qn /norestart
```

The fixed UpgradeCode enables major upgrades and blocks downgrades. Program files
live under `%LOCALAPPDATA%\Programs\CutLocal`. Runtime data lives separately
under `%LOCALAPPDATA%\CutLocal` and is deliberately preserved during uninstall.

## Signing and CI

Windows signing accepts only a 40-hex certificate thumbprint and an HTTPS
timestamp URL. GitHub Actions recognizes:

- secret `WINDOWS_SIGNING_CERTIFICATE_BASE64`
- secret `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`
- optional repository variable `REQUIRE_SIGNING=true`

The PFX exists only under the runner's temporary directory, is deleted after
import, and the certificate is removed in an unconditional cleanup step. Without
those secrets, releases are honestly labeled unsigned and retain checksums and
GitHub artifact attestations.

The current macOS community packages are neither Developer ID signed nor
notarized. Their first-launch script applies only a local ad-hoc signature after
validating the bundle. Public distribution without Gatekeeper friction requires
a native Mac, an Apple Developer ID Application certificate, notarization, and
stapling.

## Release publication

The tag workflow builds from source, reruns all gates, attests artifacts, and
creates the GitHub Release for `maliboz/CutLocal`. Do not reuse a tag or upload
older local binaries under the same version.

```powershell
git tag -a v0.1.5 -m "CutLocal 0.1.5"
git push origin v0.1.5
```

After the workflow succeeds, download the published artifacts, compare their
hashes with `SHA256SUMS.txt`, smoke-test both Windows forms, test the correct Mac
archive on native hardware, and keep the preview/unsigned limitations visible in
the release notes.
