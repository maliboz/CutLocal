# CutLocal release packaging

`Build-Release.ps1` creates the Windows x64 release artifacts:

- `CutLocal-<version>-win-x64-portable.zip`
- `CutLocal-<version>-win-x64-setup.msi`

`Build-MacRelease.ps1` creates separate self-contained Avalonia `.app` archives
for Apple Silicon and Intel Macs. See [macOS packaging](macos/README.md) for
architecture, Unix modes, signing, notarization, and first-launch details.

The Windows installer uses `WixToolset.Sdk` and `WixToolset.UI.wixext` 5.0.2,
pinned under MS-RL. No separately installed WiX application is required.

## Build

```powershell
.\installer\Build-Release.ps1 -Version 0.1.5
.\installer\Build-MacRelease.ps1 -Version 0.1.5
```

The standard release downloads U2NetP only into the ignored release cache and
accepts it only after exact byte-length and SHA-256 verification. Restricted
BiRefNet and BRIA weights are never staged in Windows or macOS release packages.
They remain explicit, acknowledgement-gated Model Manager downloads.

`-SkipInstaller` builds only the Windows portable ZIP. The per-user MSI installs
under `%LOCALAPPDATA%\Programs\CutLocal`, registers a fixed major-upgrade path,
creates a Start Menu shortcut, and offers an optional Desktop shortcut. User
models, settings, jobs, and logs under `%LOCALAPPDATA%\CutLocal` survive uninstall.

## Signing

Install a release certificate in `Cert:\CurrentUser\My`, then pass its 40-hex
SHA-1 thumbprint. Signing uses SHA-256 file digests, an HTTPS RFC 3161 timestamp,
a SHA-256 timestamp digest, and verifies every output.

```powershell
.\installer\Build-Release.ps1 `
  -Version 0.1.5 `
  -SigningCertificateThumbprint '<40 hexadecimal characters>' `
  -TimestampUrl 'https://timestamp.digicert.com'
```

GitHub Actions can import a base64 PFX from
`WINDOWS_SIGNING_CERTIFICATE_BASE64` and
`WINDOWS_SIGNING_CERTIFICATE_PASSWORD`, build and verify the release, and remove
the temporary certificate. Repository variable `REQUIRE_SIGNING=true` makes a
missing certificate fail the release. Never commit or print credentials or keys.

## Validation

```powershell
.\installer\Validate-Release.ps1 artifacts\release-work\portable\CutLocal 0.1.5
.\installer\Validate-PortableArchive.ps1 artifacts\release\CutLocal-0.1.5-win-x64-portable.zip 0.1.5
.\installer\Validate-Installer.ps1 artifacts\release\CutLocal-0.1.5-win-x64-setup.msi 0.1.5
.\installer\Validate-LicenseInventory.ps1
```

`release-manifest.json` records every staged file's byte length and SHA-256.
`SHA256SUMS.txt` covers all distributable files present in `artifacts/release`.
The portable validator checks paths and every manifest hash. The MSI validator
checks version, payload, features, shortcuts, major-upgrade rows, restricted-model
absence, and embedded-cabinet state without installing the package.

`SetupInformation.rtf` is open-source distribution information, not a
proprietary EULA. CutLocal's own code is Apache-2.0; third-party components and
model weights remain under the terms recorded in `ThirdPartyNotices.txt`.
