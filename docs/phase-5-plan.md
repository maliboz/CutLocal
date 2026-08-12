# Phase 5 — Model manager plan

## Objective

Add a manifest-driven model manager without weakening CutLocal's offline inference boundary. Catalog inspection and inference must remain network-free. Network access is allowed only after an explicit Download or Repair command in the Model Manager.

## Trust boundaries

1. Built-in catalog manifests are shipped as weight-free JSON and validated under the current distribution policy.
2. A catalog download is accepted only when id, version, URL, byte length, and SHA-256 match the locally reviewed manifest.
3. A final model file is activated only after exact-length and SHA-256 verification.
4. User-supplied ONNX files require a companion manifest and explicit license acknowledgement. CutLocal does not guess normalization, activation, or tensor behavior.
5. Inference and application startup never call the package download service.

## Storage layout

```text
%LOCALAPPDATA%\CutLocal\
  models\<model-id>\<version>\<file>.onnx
  model-manifests\<model-id>.<version>.json
  model-manifests\<model-id>.<version>.accepted
  model-quarantine\<privacy-safe-generated-name>.onnx
```

Downloads use `<file>.onnx.partial`. Custom imports use `<file>.onnx.importing` until the copied file has been hashed.

## Download state machine

```text
NotInstalled ── Download ──> Partial ── SHA-256 success ──> Installed
       ^                         │                                │
       │                         ├── Pause/cancel (retain)        ├── Delete
       │                         ├── Resume with Range            │
       │                         └── invalid/hash mismatch ──> Quarantined/Corrupted
       │                                                        │
       └──────────────────────────── Delete <────────────────────┘

Corrupted ── Repair ──> quarantine invalid final ──> Download ──> Installed
```

Resume rules:

- Send `Range: bytes=<partial-length>-` only when a partial file exists.
- Append only after `206 Partial Content` and an exact `Content-Range` start/total match.
- If a server ignores Range and returns `200`, truncate and restart once; never append.
- Accept `416` only when the partial length already equals the manifest length and the server reports the same total; then verify SHA-256.
- Preserve a bounded partial file on cancellation or transient HTTP failure.
- Reject plaintext HTTP at the initial URL and at every redirect hop.

## Optional pack selected for Phase 5

BiRefNet General Lite is the first optional balanced pack:

- upstream source code: ZhengPeng7/BiRefNet, MIT; the reviewed weight is now
  conservatively classified as non-commercial and is never bundled;
- ONNX distribution reference: danielgatis/rembg release asset;
- exact size: `224005088` bytes;
- SHA-256: `5600024376F572A557870A5EB0AFB1E5961636BEF4E1E22132025467D0F03333`;
- input: float RGB NCHW `1×3×1024×1024`;
- output policy: stable sigmoid followed by min/max normalization;
- provider declared in this phase: CPU only, until DirectML memory/performance hardening is measured.

The hash and length are measured from the complete official asset, not copied from an unreviewed third-party index. The observed MD5 also matched the rembg session's official reference value.

## Implementation slices

1. Extend the manifest with exact byte length and validated activation policy.
2. Add installation-state and operation contracts plus `ModelManagementUseCase`.
3. Merge built-in manifests with receipt-gated custom manifests.
4. Implement streaming HTTPS download, range resume, quarantine, repair, delete, and safe import.
5. Invalidate cached native sessions before replacing or deleting model files.
6. Generalize the manifest-driven adapter for BiRefNet's `sigmoid-minmax` output.
7. Add a Turkish/English, virtualized WPF Model Manager.
8. Verify fake-HTTP edge cases, offline inspection, license receipts, UI render, catalog policy, and a real BiRefNet CPU inference.

## Acceptance criteria

- Application and catalog inspection open offline.
- Installed/available state, exact size, version, license, provider compatibility, and progress are visible.
- Download, pause, resume, SHA-256 verification, quarantine, repair, delete, and custom import work.
- Cancellation retains a resumable partial file.
- No ONNX weight is committed under `assets/` or normal source history.
- BRIA remains blocked from the commercial catalog; a user-supplied noncommercial model is visible only with an acceptance receipt.
- Release build has zero warnings/errors; all relevant tests pass.

## Primary references

- [.NET HttpClient lifetime guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [HttpCompletionOption.ResponseHeadersRead](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
- [SHA256.HashDataAsync](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdataasync?view=net-10.0)
- [BiRefNet repository](https://github.com/ZhengPeng7/BiRefNet)
- [BiRefNet MIT license](https://github.com/ZhengPeng7/BiRefNet/blob/main/LICENSE)
- [rembg BiRefNet General Lite session reference](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/birefnet_general_lite.py)
