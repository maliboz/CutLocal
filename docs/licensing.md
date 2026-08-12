# Repository and model licensing analysis

Reviewed 2026-08-12. This is an engineering distribution gate, not legal advice.

## Findings

| Component or weights | Evidence | Engineering classification | Commercial bundle decision |
|---|---|---|---|
| `danielgatis/rembg` code | [Repository license is MIT](https://github.com/danielgatis/rembg/blob/main/LICENSE.txt) | Permissive code reference; notices required when copied | Behavior may be reimplemented. CutLocal does not ship or execute rembg/Python. |
| U-2-Net repository and reviewed U2NetP artifact | [Apache-2.0 LICENSE](https://github.com/xuebinqin/U-2-Net/blob/master/LICENSE), [rembg U2NetP session](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/u2netp.py) | Permissive upstream project; exact asset is pinned by byte length and SHA-256 | Included as the offline default with the upstream license and attribution. |
| rembg U2NetP ONNX asset | [`u2netp.py` pins the release URL and MD5](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/u2netp.py) | Exact technical artifact is identifiable; MD5 is insufficient for CutLocal activation | Manifest uses a locally measured SHA-256. Weight is excluded from Git. |
| BiRefNet source code | [MIT repository license](https://github.com/ZhengPeng7/BiRefNet/blob/main/LICENSE) | Permissive source code; this grant is not automatically a grant for every separately distributed weight | Retain source attribution. Do not infer a weight license from the code license. |
| Reviewed BiRefNet General Lite ONNX weight | [Upstream repository](https://github.com/ZhengPeng7/BiRefNet#onnx-conversion), [rembg session reference](https://github.com/danielgatis/rembg/blob/main/rembg/sessions/birefnet_general_lite.py) | `LicenseRef-BiRefNet-Weights-NonCommercial`; upstream documentation describes weights as non-commercial and no clearer weight-specific permissive grant was identified | Never bundle. Permit only an explicit, user-initiated, acknowledgement-gated download for non-commercial use. |
| IS-Net general-use weights | [Official DIS repository](https://github.com/xuebinqin/DIS) does not expose a clear repository LICENSE in the reviewed tree | Unknown/insufficient | Do not bundle, download, or mark commercial until a weight-specific license is recorded. |
| BRIA RMBG-2.0 weights | [Official model card states CC BY-NC 4.0 and separate commercial terms](https://huggingface.co/briaai/RMBG-2.0) | Non-commercial by default | Never bundled. The reviewed manifest permits an optional user-initiated download only after explicit warning/acknowledgement. |
| ONNX Runtime | [MIT](https://github.com/microsoft/onnxruntime/blob/main/LICENSE) | Permissive native/runtime dependency | Ship license and notices. |
| SkiaSharp/Skia | [SkiaSharp package license](https://www.nuget.org/packages/SkiaSharp) and transitive notices | Permissive with transitive notice obligations | Generate third-party notices from the locked dependency graph. |

## Enforced policy

CutLocal's original source and documentation are licensed under Apache-2.0. This
is a genuine open-source license and therefore permits commercial as well as
non-commercial use. Third-party components and model assets remain under their
own licenses and attribution requirements.

CutLocal is not a rembg fork. rembg is a technical reference and model asset
distribution source, so its MIT attribution is retained in `NOTICE` and
`ThirdPartyNotices.txt`; the Python package is not included in production.

A model manifest is invalid unless it contains a non-empty SPDX expression, source, commercial-use flag, attribution flag, HTTPS source/download URL, 64-character SHA-256, and version. Unknown means denied. CI/tool validation fails closed.

The manifest's `commercialUseAllowed` field is distribution policy metadata, not a substitute for evidence. The release process must retain a provenance record containing source URL, access date, upstream revision/release, original checksum if supplied, CutLocal SHA-256, license text snapshot, and reviewer.

`BRIA-RMBG-2.0`, `bria-rmbg`, the reviewed BiRefNet General Lite weight, or any
other non-commercial/unknown weight remains blocked from every CutLocal release
bundle. A reviewed restricted catalog entry may expose a weight-free manifest
and exact hash, but the application must obtain explicit acknowledgement before
network access. Release tooling must never stage these weights in an MSI,
portable ZIP, or macOS archive, and their terms may not be relabeled.

## Third-party notices output

`ThirdPartyNotices.txt` records the reviewed direct/native dependency and model
inventory. `installer/Validate-LicenseInventory.ps1` verifies the resolved
Windows and macOS dependency graphs in CI. Model weights do not enter Git
history; the standard U2NetP weight is acquired into an ignored cache and staged
only after exact integrity verification.
