# CutLocal macOS packaging

The macOS UI is an Avalonia front end over the same local imaging and ONNX Runtime
pipeline as the Windows application. Release archives are intentionally split by
CPU architecture and contain a self-contained `.app` bundle.

Build both unsigned preview archives from the repository root:

```powershell
./installer/Build-MacRelease.ps1 -Version 0.1.5
```

The generated `.tar.gz` files preserve the Unix executable bit even when the build
runs on Windows. A native Mac must still perform the final launch validation.
Each archive includes `FIX-CUTLOCAL.command`, an inspectable local finalizer that
validates the bundle identifier, clears quarantine only from that bundle, signs
each Mach-O file and the bundle ad hoc, verifies the result, and launches it.
Because Gatekeeper can block the `.command` file when Finder launches it, the
documented path opens Terminal explicitly and invokes the script with
`/bin/bash <dragged-script-path>`.

The Apple Silicon build uses ONNX Runtime 1.24.4. Microsoft removed prebuilt macOS
x64 binaries starting with 1.24, so the Intel build is pinned as a complete unit to
the last official x64-capable line, 1.23.2; its managed and native components are
never mixed with 1.24.

For public distribution without Gatekeeper warnings, run the build on macOS, sign
all Mach-O files and the final bundle with a Developer ID Application certificate,
then notarize and staple the archive. `CutLocal.entitlements` contains the JIT
entitlement needed when hardened runtime signing is enabled.
