# Open-source readiness report

Reviewed and verified on 2026-08-12 for CutLocal 0.1.5.

## Outcome

The source tree is suitable for initialization as the independent public GitHub
repository `maliboz/CutLocal`. It contains bilingual product documentation,
Apache-2.0 project licensing, third-party attribution, model-specific license
policy, community/security files, issue and pull-request templates, Dependabot,
CI, release automation, packaging validators, privacy documentation, and an
explicit pre-1.0 limitations statement.

This result means the reviewed gates passed in the recorded environment. It is
not a guarantee that every image, GPU, driver, locale, filesystem, or Mac will be
defect-free. It is not legal advice or a penetration-test certification.

## License and upstream position

- CutLocal original source and documentation: Apache-2.0.
- Copyright identity: `2026 CutLocal contributors`.
- rembg: MIT-licensed technical reference and model-asset host; not a fork,
  runtime dependency, bundled Python package, or subprocess.
- U2NetP: bundled offline default; exact asset is pinned by byte length and
  SHA-256 with U-2-Net Apache-2.0 attribution.
- BiRefNet source code: MIT. The reviewed General Lite ONNX weight is separately
  classified as `LicenseRef-BiRefNet-Weights-NonCommercial` because no clearer
  weight-specific permissive grant was identified. It is never bundled.
- BRIA RMBG-2.0: CC BY-NC 4.0; never bundled.

Restricted weights are exposed only as weight-free manifests. A download is
user-initiated, acknowledgement-gated, HTTPS-only, and activated only after
exact length and SHA-256 verification. Project and model licenses are deliberately
kept separate.

## Security and privacy review

- Production processing is local and in-process; there is no image-upload API,
  product telemetry, Python runtime, rembg process, or local HTTP service.
- Model network access is confined to explicit Model Manager download/repair
  actions and reviewed local manifests. Redirects remain HTTPS and are bounded.
- Model and output writes use bounded streaming, temporary files, integrity
  checks, and atomic replacement where applicable.
- Static repository scans found no committed credential pattern, private key,
  signing certificate, model weight, generated package, or personal input image
  in the publishable file set.
- NuGet audit checked direct and transitive dependencies in the solution and WiX
  installer project: 2 reports, 0 known vulnerable packages.
- GitHub Actions dependencies are pinned to full commit SHAs; Dependabot remains
  configured for NuGet and Actions updates.
- Release tooling rejects restricted model weights from Windows and macOS
  packages and emits SHA-256 sums for every distributable artifact.

## Verification results

| Gate | Result |
|---|---|
| Source formatting | Passed |
| Release solution build | Passed, 0 warnings / 0 errors |
| Unit tests | 76 passed / 0 failed / 0 skipped |
| Integration tests | 17 passed / 0 failed / 0 skipped |
| Golden-image tests | 1 passed / 0 failed / 0 skipped |
| Stress tests | 3 passed / 0 failed / 0 skipped |
| Total automated tests | 97 passed / 0 failed / 0 skipped |
| Model manifest policy | 3 passed, including restricted-license gates |
| Project and third-party license inventory | Passed; 70 production packages / 4 canonical licenses |
| NuGet vulnerability audit | Passed; 0 known vulnerable packages |
| Portable archive validation | Passed; 465 files / 464 manifest hashes |
| MSI database validation | Passed; 465 files / 20 components / 2 shortcuts |
| MSI restricted models | None bundled |
| MSI cabinet | Embedded |
| macOS arm64/x64 archive creation | Passed |
| macOS restricted models | No restricted weights bundled |

## Locally generated unsigned artifacts

These files are generated under ignored `artifacts/` and must not be committed.
The public release workflow should rebuild them from the tagged source.

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `CutLocal-0.1.5-win-x64-portable.zip` | 91,959,422 | `4B1F7B7495C828423C5800BBC5CD05D36B3C8C8DD1963B79A5B1E0C2DF5BE937` |
| `CutLocal-0.1.5-win-x64-setup.msi` | 79,569,356 | `492D8F20E0D207A54AEE922C7A6FD3B3270AE2729C5A249FB14B451FB16D53AB` |
| `CutLocal-0.1.5-macos-arm64.tar.gz` | 65,379,954 | `E32813CA7A87FB046D72A54D6860FFFC03EBD5F5467A68511C0EB4D7F9F2F2B1` |
| `CutLocal-0.1.5-macos-x64.tar.gz` | 69,559,965 | `303211B207DEE46E6E1F23895FD5826F001A35BCEBD5E311FEAF49CA543E55D8` |

The Windows executable and MSI report `NotSigned`. The macOS bundles are not
Developer ID signed or notarized. Checksums and GitHub artifact attestations
improve transparency but do not replace platform code signing.

## Honest remaining boundaries

- GitHub-hosted CI and release workflows cannot run until the repository exists.
- The current packages must still be downloaded from the eventual GitHub Release
  and smoke-tested once more as the exact hosted artifacts.
- macOS first-launch recovery was exercised on a real Mac, but broad Apple
  Silicon/Intel, filesystem, accessibility, and OS-policy coverage remains
  incomplete.
- Windows is the primary and most extensively tested interface; macOS does not
  currently expose the complete Windows batch workflow.
- The stable public file path is PNG input to transparent PNG output.
- Background-removal quality depends on the selected model and image. Automated
  correctness tests do not constitute a broad real-photo quality benchmark.
- No code-signing certificate or Apple notarization credential was supplied.

## Remaining maintainer actions

1. Initialize Git locally and inspect the exact staged source list.
2. While authenticated as `maliboz`, create a blank public `CutLocal` repository.
3. Add `https://github.com/maliboz/CutLocal.git` as `origin` and push `main`.
4. Enable Discussions, private vulnerability reporting, Dependabot/security
   features, and a `main` ruleset requiring the CI status check.
5. Confirm GitHub-hosted CI passes, then push the immutable `v0.1.5` tag.
6. Verify the hosted artifact hashes and repeat clean Windows/native Mac smoke
   tests before announcing the release.
7. Keep every public download explicitly labeled unsigned until signing and, on
   macOS, notarization are configured.

Follow `docs/open-source-release-checklist.md` for the exact commands and account
safety checks. A rembg fork is neither required nor appropriate for this project.
