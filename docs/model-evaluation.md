# Model comparison and quality evaluation

No model becomes the default because of reputation or a single latency result.
U2NetP is the current offline baseline. Restricted weights may be evaluated by a
user who accepts their terms, but they are not eligible for standard release
bundles.

## Candidate gate

| Candidate | License/distribution gate | Input behavior to verify | Current status |
|---|---|---|---|
| U2NetP / Silueta | U-2-Net Apache-2.0 provenance and notices | 320x320 RGB, ImageNet normalization, first-output min/max | Bundled baseline |
| BiRefNet General Lite | Source code is MIT; reviewed weight is conservatively `LicenseRef-BiRefNet-Weights-NonCommercial` | 1024x1024 RGB, sigmoid then min/max | Optional non-commercial download; never bundled |
| BiRefNet General / Portrait | Require a weight-specific license, provenance, hash, and resource review | Model-specific normalization and output mapping | Not distributed |
| IS-Net General Use | No sufficiently clear reviewed weight license | Model-specific normalization and output mapping | Blocked from distribution |
| BRIA RMBG-2.0 | CC BY-NC 4.0; separate permission required for commercial use | Reviewed 1024x1024 ONNX; CPU path | Optional non-commercial download; never bundled |

## Corpus

Use versioned, redistributable images with attribution records: people with
varied hair and skin tones, fur, translucent glass, fine products, dark-on-dark
and light-on-light subjects, multiple foreground objects, synthetic hard edges,
and images at 512x512, 1920x1080, 4000x3000, and 7680x4320. Keep tuning and
holdout sets separate. Never commit private user images.

## Measurements

For each model, provider, and hardware tuple, record cold and warm latency,
decode, preprocess, tensor creation, inference, mask resize, refinement,
composition, encode, throughput, peak working set, GPU memory where available,
and handle-count delta.

Quality metrics include mask MAE, IoU at a documented threshold, boundary
F-score at 1/2/4-pixel tolerances normalized by image diagonal, alpha difference
on soft edges, and targeted edge crops. Human blind review resolves cases where
aggregate metrics hide halos, missing hair, or foreground holes.

## Selection rule

1. Reject license, provenance, provider, memory, determinism, or recovery failures.
2. Require no critical holdout-category quality regression.
3. Determine the quality, p50/p95 latency, throughput, and memory Pareto frontier.
4. Prefer the lower-memory eligible model when confidence intervals overlap.
5. Store the result and rationale in a versioned model-policy record.
6. Never silently change the default model after an application update.

The C# adapter may be compared with a trusted upstream Python implementation in
tests only. Python is not a production or packaging dependency. A model that
scores well remains ineligible for distribution when its license or exact weight
provenance is unclear.
