# Privacy statement

CutLocal is designed for local image processing.

## Data processed

The application reads only files and folders selected by the user, decodes PNG
pixels locally, runs local ONNX inference, and writes transparent PNG outputs to
the selected folder. Image contents are not sent to CutLocal maintainers or a
cloud inference service.

On Windows, CutLocal may store non-secret preferences, model files, sanitized
logs, and interrupted-job recovery state under `%LOCALAPPDATA%\CutLocal`.
Program files are installed under `%LOCALAPPDATA%\Programs\CutLocal` by the MSI.
On macOS, application data follows the platform-specific local application-data
directory resolved by .NET. The application does not sync this data to a CutLocal
service.

## Network behavior

Image processing does not require the network. The application uses the network
only when the user explicitly starts a model download or repair in Model Manager.
Those model files are accepted only after their manifest-pinned byte length and
SHA-256 match.

Downloading CutLocal itself from GitHub and GitHub's web analytics are governed
by GitHub's policies, not by the desktop application's runtime behavior.

## Telemetry and logs

CutLocal does not contain product analytics, advertising identifiers, accounts,
or usage telemetry. Logs are local and should not contain image content or full
input paths. Before attaching logs to a public issue, users should still inspect
and redact any personal information.

## Deletion

Windows uninstall removes program files and shortcuts but preserves user data to
avoid destroying models, settings, logs, or recovery state unexpectedly. A user
can delete `%LOCALAPPDATA%\CutLocal` manually after uninstall when those files
are no longer needed. On macOS, deleting `CutLocal.app` does not automatically
delete local application data.

This document describes the software's designed behavior; it is not a promise
about modified third-party builds. Verify the source, release checksum, and
publisher of any binary you run.
