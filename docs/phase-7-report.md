# Phase 7 — release report

> Historical 0.1.0 implementation evidence. Current 0.1.5 release workflows,
> all-platform packaging, and restricted-model policy supersede artifact values
> and optional balanced-pack behavior in this report.

## Outcome

Phase 7 is technically complete. CutLocal now produces a validated self-contained portable ZIP and per-user WiX MSI, opens offline with a trusted default model, carries release notices/licenses, supports feature selection and major upgrades, preserves user data on uninstall, and has CI/release/nightly workflows with dependency audit, optional Authenticode signing, artifact attestation, and secret cleanup.

The locally produced artifacts are intentionally unsigned because no publisher certificate was supplied. The project is now licensed under Apache-2.0, so the public open-source release does not require a proprietary EULA. A publisher certificate remains an optional trust improvement rather than a publishing blocker.

## Final artifacts

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `CutLocal-0.1.0-win-x64-portable.zip` | 91,594,903 | `383212067F05500200B2A5662190DBC5FC7DC5CC86283C12E9B1847433713EB2` |
| `CutLocal-0.1.0-win-x64-setup.msi` | 79,061,428 | `2B3B3DB5B29F95F90B5EDC5352668762A54B01247DF5F1F47356FF3845E4B70A` |
| `SHA256SUMS.txt` | 203 | `CFDDE1E772065DD0008164A64A9B795F6DD0BCD3F426656E4561D9F9151A986F` |

The portable archive contains 462 files. Its manifest covers the other 461 entries; every entry length and SHA-256 was streamed and compared successfully. The MSI database contains 462 files, 20 deterministic components, two shortcuts, two major-upgrade rows, a required fast-model feature, an optional desktop feature, project `LICENSE`/`NOTICE`, and one embedded cabinet. Generated uninstall rows cover every application directory. ICE91 alone is suppressed because the package is fixed to `Scope="perUser"`; all other MSI ICE validation remains enabled.

## Publish decision

Both publish profiles use `net10.0-windows`, `win-x64`, `SelfContained=true`, multi-file output, ReadyToRun, no trimming, no AOT, no symbols, and no single-file extraction/compression.

| Candidate | Files | Publish bytes | Approx. size | Five hidden-start measurements (ms) |
|---|---:|---:|---:|---|
| IL-only | 453 | 301,133,922 | 287.18 MiB | 1631.33, 853.48, 872.44, 822.67, 800.19 |
| ReadyToRun | 453 | 305,349,266 | 291.20 MiB | 1104.71, 815.01, 793.04, 726.00, 694.05 |

ReadyToRun was selected for roughly 4 MiB extra publish size and better first/median startup. PDB removal plus packaging reduces the final ZIP substantially. Multi-file output avoids fragile extraction and keeps the native .NET/ONNX Runtime/DirectML/Skia probing layout stable.

## Offline model activation

- The release build acquires U2NetP only over HTTPS and accepts exactly 4,574,861 bytes with SHA-256 `309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8`.
- Manifest identity fields are safe path segments; traversal input was rejected before any download or write.
- App startup seeds a valid bundled model into `%LOCALAPPDATA%\CutLocal\models\u2netp\1` through a flushed `.seeding` file and atomic move.
- A corrupt per-user built-in copy is repaired from the trusted bundle; a corrupt bundle fails closed; an absent optional bundle is ignored.
- Four new unit tests cover those seeding/repair/fail-closed paths.

## Installer lifecycle evidence

| Scenario | Result |
|---|---|
| Standard silent install | Exit code `0`; EXE, model, Start Menu shortcut present; Desktop shortcut absent |
| Final installed application startup | Real `CutLocal` main window created; non-zero window handle; process responsive; project LICENSE/NOTICE and bundled model verified |
| Desktop feature install | `ADDLOCAL=ApplicationFeature,FastModelFeature,DesktopShortcutFeature` created both shortcuts |
| File-bearing major upgrade | 0.1.0 upgraded to 0.1.1; `upgrade-probe-0.1.1.txt` installed; exactly one registered CutLocal product at 0.1.1 |
| Downgrade metadata | Fixed UpgradeCode plus `MajorUpgrade`; lower versions blocked by authored policy |
| Uninstall | Exit code `0`; install directory and shortcuts removed; registered product count returned to zero |
| User-data policy | `%LOCALAPPDATA%\CutLocal` model and log data remained intact after uninstall |

Windows Installer provides repair and Apps & Features integration; there is no separate uninstaller EXE. The install root is `%LOCALAPPDATA%\Programs\CutLocal` and does not require elevation.

## Integrity, licensing, and signing

- `release-manifest.json` records path, byte length, and SHA-256 for every staged payload.
- Portable validation rejects traversal/absolute/duplicate paths, PDBs, `.partial`, and `.seeding` entries.
- MSI validation reads Windows Installer tables and checks identity, payload/features/shortcuts, upgrade rows, and embedded media.
- Production dependency inventory resolved 46 packages and found all required notices plus canonical MIT, Apache-2.0, BSD-3-Clause, and MS-RL texts.
- Both solution and installer NuGet vulnerability reports returned zero known vulnerable packages.
- WiX Toolset SDK/UI 5.0.2 is pinned under MS-RL. The maintenance-fee program begins with WiX 6, avoiding an unapproved commercial cost/EULA transition.
- Signing accepts only a certificate-store thumbprint, uses SHA-256 file/timestamp digests and an HTTPS RFC 3161 timestamp, then verifies Authenticode policy.
- GitHub release CI imports a secret PFX beneath the runner temp directory, deletes the file immediately, and removes the certificate in an `always()` step. `REQUIRE_SIGNING=true` converts an absent certificate into a hard failure.

## CI/CD delivered

- `.github/workflows/ci.yml`: restore, ReadyToRun restore, WiX restore, format, Release build, 92 tests, model/license validation, two dependency audits, portable/MSI build, artifact upload.
- `.github/workflows/release.yml`: tag/manual version resolution, complete source gates, optional balanced pack, optional required signing, signature verification, provenance attestation, artifact upload, certificate cleanup.
- `.github/workflows/nightly.yml`: integration/golden/stress tests plus the large optional BiRefNet General Lite package build.

The workflows use current official major versions checked during Phase 7: `actions/checkout@v6`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v7`, and `actions/attest@v4`. All three YAML documents parsed locally. A hosted GitHub run cannot be executed from this workspace because no repository/remote workflow run was provided.

## Final verification

- Release build: success, `0` warnings, `0` errors.
- Complete regression suite: `92/92` passed, `0` skipped.
  - Unit/UI: `71/71`
  - Integration: `17/17`
  - Golden image: `1/1`
  - Stress: `3/3`
- Formatter verification: passed after normalizing Phase 7 C# files to repository-required CRLF.
- Historical model manifest result: BiRefNet General Lite was accepted under a
  former MIT-weight assumption. Current policy classifies that weight as
  `LicenseRef-BiRefNet-Weights-NonCommercial` and excludes it from every release.
  U2NetP (`1 MiB`, Apache-2.0) remains accepted.
- License inventory: 46 resolved production packages, four canonical license texts, WiX 5.0.2 pin — passed.
- Dependency audit: two reports, zero known vulnerable packages.
- Portable archive: 462 files, 461 manifest hashes — passed.
- MSI database: 462 files, 20 components, two shortcuts, two major-upgrade rows — passed.
- Final MSI install/start validation: passed; install exit code `0`, real main window created and responsive, LICENSE/NOTICE present, user data preserved. MSI uninstall lifecycle remains passed with exit code `0` and install-root removal.
- Production scan: no `Process.Start`, `NamedOnnxValue`, model weight, PFX/private-key file, or enabled ONNX telemetry path. `HttpClient` remains confined to the explicit Model Manager download service; inference does not use it.

## Commands executed

Representative acceptance commands (installer lifecycle commands also wrote verbose MSI logs beneath `artifacts`):

```powershell
.\.dotnet\dotnet.exe format CutLocal.sln --verify-no-changes --no-restore --verbosity minimal
.\.dotnet\dotnet.exe build CutLocal.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.UnitTests\CutLocal.UnitTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.IntegrationTests\CutLocal.IntegrationTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.GoldenImageTests\CutLocal.GoldenImageTests.csproj --configuration Release --no-build --no-restore
.\.dotnet\dotnet.exe test tests\CutLocal.StressTests\CutLocal.StressTests.csproj --configuration Release --no-build --no-restore
.\installer\Build-Release.ps1 -Version 0.1.0 -SkipInstaller
.\installer\Generate-WixSource.ps1 -StageRoot artifacts\release-work\installer\CutLocal -OutputPath artifacts\release-work\wix\HarvestedFiles.wxs
.\.dotnet\dotnet.exe build installer\CutLocal.Setup.wixproj --configuration Release
.\installer\Validate-PortableArchive.ps1 artifacts\release\CutLocal-0.1.0-win-x64-portable.zip 0.1.0
.\installer\Validate-Installer.ps1 artifacts\release\CutLocal-0.1.0-win-x64-setup.msi 0.1.0
.\installer\Validate-LicenseInventory.ps1
.\installer\Assert-DependencyAudit.ps1 -ReportPath artifacts\audit\solution.json,artifacts\audit\installer.json
msiexec.exe /i CutLocal-0.1.0-win-x64-setup.msi /qn /norestart
msiexec.exe /x {product-code} /qn /norestart
```

## Changed files

### Runtime and tests

- `Directory.Build.props`
- `Directory.Packages.props`
- `src/CutLocal.App/App.xaml.cs`
- `src/CutLocal.App/Properties/PublishProfiles/Portable.pubxml`
- `src/CutLocal.App/Properties/PublishProfiles/Installer.pubxml`
- `src/CutLocal.Contracts/IBundledModelSeeder.cs`
- `src/CutLocal.Infrastructure/ApplicationPaths.cs`
- `src/CutLocal.Infrastructure/BundledModelSeeder.cs`
- `src/CutLocal.Infrastructure/ServiceCollectionExtensions.cs`
- `tests/CutLocal.UnitTests/BundledModelSeederTests.cs`

### Packaging, licensing, and CI

- `installer/Acquire-ReleaseModel.ps1`
- `installer/Assert-DependencyAudit.ps1`
- `installer/Build-Release.ps1`
- `installer/CutLocal.Setup.wixproj`
- `installer/Generate-WixSource.ps1`
- `installer/Package.wxs`
- `installer/README.md`
- `installer/SetupInformation.rtf`
- `installer/Sign-Release.ps1`
- `installer/Validate-Installer.ps1`
- `installer/Validate-LicenseInventory.ps1`
- `installer/Validate-PortableArchive.ps1`
- `installer/Validate-Release.ps1`
- `assets/licenses/MIT.txt`
- `assets/licenses/Apache-2.0.txt`
- `assets/licenses/BSD-3-Clause.txt`
- `assets/licenses/MS-RL.txt`
- `ThirdPartyNotices.txt`
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/workflows/nightly.yml`

### Documentation

- `README.md`
- `docs/phase-7-plan.md`
- `docs/release.md`
- `docs/phase-7-report.md`
- `docs/test-matrix.md`

## Known risks and external release gates

1. Local artifacts are unsigned. A real publisher certificate and public timestamp operation are required to turn the validated signing-ready path into signed binaries.
2. The artifacts are unsigned and may trigger Windows SmartScreen. Public release notes must say this plainly and publish SHA-256 sums until a trusted publisher certificate is available.
3. WiX 5 avoids the WiX 6+ maintenance fee but its upstream consumer security-update window ended on 5 February 2026. The current NuGet audit reports no known issue; the owner should choose between continuing the pinned MS-RL toolchain, accepting a current WiX EULA/fee tier, or purchasing supported tooling before long-term commercial maintenance.
4. The actual 232 MiB balanced pack was not downloaded during this local standard-release run. Its manifest/license policy and conditional MSI authoring passed; the scheduled nightly workflow performs the full hash-verified package build.
5. Lifecycle testing ran on the current Windows host. Windows 10/11 build-matrix execution requires hosted/self-hosted runners beyond this workspace.

## Acceptance decision

All Phase 7 technical acceptance criteria pass. Phase 7 is accepted as a release candidate and the implementation roadmap is complete. The Apache-2.0 open-source release may be published unsigned with clear checksum and SmartScreen guidance; code signing can be added later without changing application architecture.

## Official references used

- .NET publish and single-file behavior: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish and https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- SignTool digest/timestamp requirements: https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
- WiX 5 release/license package: https://www.nuget.org/packages/WixToolset.Sdk/5.0.2 and https://github.com/wixtoolset/wix/blob/main/LICENSE.TXT
- WiX maintenance/support policy: https://docs.firegiant.com/wix/whatsnew/ and https://docs.firegiant.com/wix/
- GitHub artifacts and secrets: https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts and https://docs.github.com/en/actions/concepts/security/secrets
- .NET dependency audit command: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list
