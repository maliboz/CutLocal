# Open-source publishing checklist

This checklist publishes the prepared source as the independent public
repository `maliboz/CutLocal`. CutLocal is not a rembg fork: it is an independent
C#/.NET implementation and retains upstream attribution where required.

## 1. Verify the exact source tree

- Read `LICENSE`, `NOTICE`, `ThirdPartyNotices.txt`, and `docs/licensing.md`.
- Confirm no private images, personal logs, certificates, credentials, model
  weights, local SDKs, or generated packages are staged.
- Confirm the intended release version is `0.1.5` everywhere.
- Run the complete gate from the repository root:

  ```powershell
  dotnet restore CutLocal.sln
  dotnet restore src\CutLocal.App\CutLocal.App.csproj --runtime win-x64 -p:PublishReadyToRun=true
  dotnet restore installer\CutLocal.Setup.wixproj
  dotnet format CutLocal.sln --verify-no-changes --no-restore --verbosity minimal
  dotnet build CutLocal.sln --configuration Release --no-restore
  dotnet test CutLocal.sln --configuration Release --no-build --no-restore
  dotnet run --project tools\CutLocal.ModelTools\CutLocal.ModelTools.csproj `
    --configuration Release --no-build -- assets\models\manifests --noncommercial
  .\installer\Validate-LicenseInventory.ps1
  .\installer\Build-Release.ps1 -Version 0.1.5
  .\installer\Build-MacRelease.ps1 -Version 0.1.5
  ```

Restricted BiRefNet and BRIA weights must not appear in any MSI, ZIP, TAR, Git
tree, or GitHub artifact. U2NetP is acquired only into the ignored release cache
and accepted after exact length and SHA-256 verification.

## 2. Create the local Git history

From this folder:

```powershell
git init -b main
git add .
git status --short
git diff --cached --check
git commit -m "feat: publish CutLocal 0.1.5"
```

Before committing, inspect the staged list. `.onnx`, `.msi`, `.zip`, `.tar.gz`,
`.pfx`, logs, `artifacts/`, `.dotnet/`, `.tools/`, `bin/`, and `obj/` must not
appear. Do not store a GitHub token in a command, file, or remote URL.

## 3. Create `maliboz/CutLocal`

While signed into the `maliboz` GitHub account, create a blank public repository
named `CutLocal`. Do not ask GitHub to add a README, license, or `.gitignore`.
Then use the URL shown by GitHub:

```powershell
git remote add origin https://github.com/maliboz/CutLocal.git
git remote -v
git push -u origin main
```

Before pushing, run `gh auth status` if GitHub CLI is installed, or verify the
account shown by Git Credential Manager. Because two accounts are used on this
computer, do not assume that a cached browser or Git credential belongs to
`maliboz`. A passkey or Git Credential Manager is preferable to a personal
access token in a command.

## 4. Configure the GitHub repository

Recommended About values:

- Description: `Private, local background removal for Windows and macOS with ONNX Runtime.`
- Website: leave empty until a real project page exists.
- Topics: `background-removal`, `windows`, `macos`, `wpf`, `avalonia`, `dotnet`,
  `onnx-runtime`, `directml`, `computer-vision`, `privacy`, `offline-first`.

Repository settings:

- Enable Issues and Discussions.
- Enable private vulnerability reporting under Security.
- Enable Dependabot alerts, security updates, and secret scanning if available.
- Keep Actions enabled for workflows from this repository.
- Set `main` as the default branch.
- Set repository Actions workflow permissions to read by default; allow the
  release workflow's explicit `contents: write` permission.
- Add a `main` ruleset requiring `CI / build-test-package`, resolved
  conversations, and pull requests; block force pushes and branch deletion.

For a one-person repository, requiring an external approval can block the only
maintainer. Begin with CI and resolved-conversation requirements, then add a
reviewer requirement when another maintainer joins.

## 5. Publish version 0.1.5

First confirm that the main-branch CI run passes. Then create one immutable tag:

```powershell
git tag -a v0.1.5 -m "CutLocal 0.1.5"
git push origin v0.1.5
```

The tag workflow rebuilds from source, reruns format/tests/legal gates, builds
Windows and macOS artifacts, generates SHA-256 sums, attests the artifacts, and
creates the GitHub Release. Never reuse a tag or upload an older local binary
under the same version.

After the workflow succeeds:

- download every published artifact and verify it against `SHA256SUMS.txt`;
- smoke-test the MSI and portable ZIP on a clean Windows profile;
- test each macOS architecture archive on matching native hardware;
- confirm the release clearly states that Windows binaries are unsigned when no
  certificate was configured;
- confirm macOS packages are described as unsigned and not notarized; and
- keep the pre-1.0 limitations and restricted-model terms visible.

## 6. Signing status

A paid certificate is not required for an honest open-source release. Until one
is available, publish only reproducible workflow-built packages, retain checksums
and GitHub artifact attestations, and label the binaries as unsigned. Do not tell
users to bypass operating-system warnings without first validating the package
checksum and source.

When a Windows certificate becomes available, store its base64 PFX and password
only in the documented GitHub Actions secrets. Public macOS distribution without
Gatekeeper friction additionally requires Apple Developer ID signing,
notarization, and stapling on native Apple tooling.

## 7. Maintain trust after launch

- Publish reproducible issue reports and measurable release notes.
- Add `good first issue` only to bounded tasks with acceptance criteria.
- Publish hardware, inputs, settings, and methodology with quality or speed claims.
- Never use star exchanges, spam, copied marketing, or undisclosed benchmarks.
- Update notices whenever a dependency, model, or package source changes.

Repository trust comes from a useful product, safe downloads, transparent
limitations, and consistent maintenance.
