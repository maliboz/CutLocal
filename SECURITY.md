# Security policy

CutLocal handles untrusted image and model files and therefore treats security
reports seriously.

## Supported versions

| Version | Supported |
|---|---|
| Latest published release | Yes |
| `main` | Best effort |
| Older releases | No |

Until version 1.0, users should update to the newest release for security fixes.

## Reporting a vulnerability

Use GitHub's private **Security > Report a vulnerability** flow. Please do not
open a public issue for a suspected vulnerability.

Include, when possible:

- affected version, operating system, architecture, and package type;
- reproduction steps or a minimal proof of concept;
- expected and observed impact;
- whether a malicious PNG, ONNX model, manifest, path, archive, or installer is involved;
- relevant logs with personal paths and image content removed; and
- any suggested mitigation.

You should receive an acknowledgement within 7 days. The project will aim to
triage validated reports within 14 days, coordinate a fix and disclosure, and
credit the reporter if requested. These are best-effort targets for a
community-maintained project, not a service-level agreement.

## Security boundaries

- Production inference is in-process and local; CutLocal has no image-upload API
  or telemetry path.
- Downloadable models are HTTPS-only and activated only after exact SHA-256 and
  length verification.
- Model manifests are data, not executable plug-ins.
- Output files and persisted JSON state are written through temporary files and
  atomically replaced.
- Release signing is supported, but an unsigned build must be treated as
  untrusted until its published SHA-256 is verified.

Questions about ordinary crashes or incorrect masks belong in the bug tracker,
not the private security channel.
