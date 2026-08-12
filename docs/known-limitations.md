# Known limitations and verification boundary

Last reviewed: 2026-08-12.

CutLocal 0.1.5 is a pre-1.0 public preview. The project uses automated tests,
package validation, manual launch checks, integrity checks, and explicit failure
policies to reduce risk. Those controls cannot prove that the application is
free of defects on every input and hardware combination.

## Current product boundary

- The stable input and output path is PNG to transparent RGBA PNG.
- Windows 10/11 x64 is the primary platform. It supports CPU and DirectML with a
  controlled CPU fallback for eligible GPU failures.
- macOS 14+ Apple Silicon and Intel builds use CPU inference and a separate
  Avalonia interface. They do not currently provide the complete Windows batch
  interface.
- Windows binaries may be unsigned. macOS archives are not Developer ID signed
  or notarized. Checksums and source-built GitHub artifacts improve transparency
  but do not replace platform code signing.
- U2NetP is the only model weight bundled in standard packages. Restricted
  optional weights require a user-initiated download and acknowledgement.

## Quality boundary

Background removal is probabilistic segmentation, not a lossless geometric
operation. Results can degrade on fine hair, fur, glass, translucent materials,
motion blur, low contrast, foreground-colored backgrounds, small disconnected
parts, or multiple overlapping subjects. Threshold and feather controls can
refine a mask but cannot restore details the selected model never detected.

The model input resolution is independent from output dimensions. Original
pixels are preserved, but a low-resolution mask can still limit edge detail.

## Test boundary

Automated coverage includes unit, integration, deterministic golden-image,
stress/resource, UI-render, manifest, package, installer, and dependency checks.
Some cases remain hardware-dependent or manual:

- broad Intel, AMD, and NVIDIA DirectML driver coverage;
- long-duration real-photo batches on many memory sizes;
- native macOS UI automation and accessibility validation;
- Intel Mac and unusual filesystem/security-policy configurations;
- a large redistributable real-image quality corpus; and
- signed/notarized public distribution paths.

An automated pass means the tested contracts passed in the recorded environment.
It does not guarantee identical performance, quality, or stability elsewhere.

## Reporting a missed issue

Use the GitHub bug form and include:

- CutLocal version and package type;
- operating system version and CPU architecture;
- CPU, GPU, memory, selected model, and inference provider;
- input dimensions and non-sensitive characteristics of the image;
- exact reproduction steps and expected/actual behavior; and
- sanitized Terminal output or logs.

Do not post private images, secrets, full personal paths, or unredacted logs.
Report suspected security vulnerabilities privately through the process in
[SECURITY.md](../SECURITY.md).

## Türkçe özet

CutLocal 0.1.5, 1.0 öncesi bir genel önizlemedir. Otomatik testler ve paket
doğrulamaları riski azaltır ancak her görsel, GPU, sürücü ve donanımda hatasızlığı
kanıtlamaz. Kararlı hat şu anda PNG girdi ve saydam PNG çıktıdır. Windows birincil
platformdur; macOS arayüzü ve gerçek donanım matrisi daha sınırlıdır. İmzasız
paketlerde checksum doğrulanmalı, macOS ilk açılış talimatları izlenmelidir.

Hata bildirirken sürüm, işletim sistemi, mimari, CPU/GPU, bellek, model,
sağlayıcı, girdi boyutları, yeniden üretme adımları ve temizlenmiş log/Terminal
çıktısı ekleyin. Özel görsel veya kişisel dosya yolu paylaşmayın.
