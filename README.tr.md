<div align="center">

<img src="assets/branding/cutlocal-logo.png" alt="CutLocal logosu" width="112" />

# CutLocal

**Windows ve macOS için gizlilik odaklı, yerel arka plan kaldırma. Bulut çıkarımı, Python çalışma zamanı, hesap ve telemetri yoktur.**

[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-7868FF)](#platform-desteği)
[![CI](https://github.com/maliboz/CutLocal/actions/workflows/ci.yml/badge.svg)](https://github.com/maliboz/CutLocal/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Lisans](https://img.shields.io/badge/kaynak-Apache--2.0-blue)](LICENSE)
[![Gizlilik](https://img.shields.io/badge/işleme-yerel-2ea44f)](#gizlilik-ve-ağ-davranışı)

[İndir](#indirme-ve-kurulum) · [Derle](#kaynak-koddan-derleme) · [Katkı](CONTRIBUTING.md) · [English](README.md)

</div>

CutLocal, görsellerin arka planını kullanıcının bilgisayarında kaldıran açık
kaynaklı bir masaüstü uygulamasıdır. ONNX Runtime uygulamanın kendi işlemi içinde
çalışır ve üretilen alfa maskesi özgün çözünürlüklü piksellere uygulanır. Üretim
uygulaması Python çağırmaz, rembg çalıştırmaz, yerel HTTP sunucusu kullanmaz,
görsel yüklemez ve ürün analitiği toplamaz.

![CutLocal tek görsel çalışma alanı](docs/images/cutlocal-single.png)

> **Sürüm durumu:** CutLocal 0.1.5 genel önizleme adayıdır. Kaynak kod, otomatik
> testler, Windows paketleri ve macOS arşiv yapısı doğrulanmış; macOS ilk açılış
> kurtarma yolu gerçek bir Mac üzerinde denenmiştir. Bu doğrulamalar her görsel,
> GPU, sürücü, dil veya donanım birleşiminin hatasız olduğunu kanıtlamaz.
> [Bilinen sınırlamalar](#bilinen-sınırlamalar) bölümünü okuyun ve yeniden
> üretilebilir sorunları issue olarak bildirin.

## Neden CutLocal

- Görsel çözme, çıkarım, maske iyileştirme ve dışa aktarma yerel olarak yapılır.
- Windows ve macOS sürüm arşivleri self-contained'dır; son kullanıcı Python veya
  ayrı bir .NET çalışma zamanı kurmaz.
- Çıktı boyutları girdi boyutlarıyla aynıdır. Modelin `320×320` veya `1024×1024`
  tensör boyutu çıktı tuvalini değil, maske ayrıntısını etkiler.
- Model indirmeleri manifest tabanlıdır; etkinleştirmeden önce tam dosya boyutu ve
  SHA-256 değerinin eşleşmesi gerekir.
- Sınırlı kuyruklar, iptal, atomik çıktılar, çökme kurtarma ve kontrollü yerel
  kaynak ömrü uzun işlemlerdeki hata riskini azaltır.
- CutLocal kodunun, bağımlılıkların ve her model ağırlığının lisansı ayrı izlenir;
  bir depo lisansı bütün model dosyaları için genel izin kabul edilmez.

## Platform desteği

| Platform | Arayüz | Çıkarım sağlayıcıları | Dağıtım | Mevcut durum |
|---|---|---|---|---|
| Windows 10/11 x64 | WPF | CPU, CPU geri dönüşlü DirectML | Kullanıcı bazlı MSI, taşınabilir ZIP | Birincil ve en kapsamlı test edilen platform |
| macOS 14+ Apple Silicon | Avalonia | CPU | `.tar.gz` içinde self-contained `.app` | Önizleme; imzasız ve noterlenmemiş |
| macOS 14+ Intel | Avalonia | CPU | `.tar.gz` içinde self-contained `.app` | Önizleme; imzasız ve noterlenmemiş |

Windows ve macOS arayüzleri aynı domain, kalıcılık, görüntü işleme, model
doğrulama ve ONNX çıkarım çekirdeğini kullanır. DirectML yalnızca Windows'tadır.

## Özellikler

### Görsel iş akışı

- PNG dosyaları için tek görsel ve sınırlı kuyruklu toplu işleme
- Windows'ta sürükle-bırak, dosya/klasör seçimi ve panodan yapıştırma
- Önce/sonra karşılaştırması, maske görünümü, sığdırma, yakınlaştırma ve kaydırma
- Eşik, yumuşatma, sert kesim, ters çevirme ve kenar kontrolleri
- Özgün boyutta saydam RGBA PNG çıktısı
- Çakışma güvenli adlandırma, Unicode yollar, uzun yol desteği ve atomik yazma
- Duraklatma, sürdürme, iptal, başarısızları yeniden deneme ve kesilen işi kurtarma
- İngilizce/Türkçe Windows kaynakları ve yüksek DPI uyumlu sunum

### Çalışma zamanı ve dayanıklılık

- `OrtValue` API'si üzerinden uygulama içi ONNX Runtime
- CPU tabanı ve Windows DirectML aygıt keşfi
- Uygun GPU aygıt kaybı veya bellek yetersizliği hatalarından sonra bir kontrollü
  CPU yeniden denemesi
- Sınırlı önbellekte yeniden kullanılan ve önceden ısıtılan oturumlar
- Ertelenmiş görsel çözme, sınırlı iş kuyrukları ve havuzlanmış maske/tensör belleği
- Hash doğrulamalı model indirme, onarma, kaldırma ve özel ONNX içe aktarma
- Görsel içeriği içermeyen ve otomatik gönderilmeyen yerel yapılandırılmış günlükler

### Sürüm mühendisliği

- Yerel bağımlılıkları kararlı arama düzeninde tutan self-contained .NET 10 sürümleri
- Windows x64 için kullanıcı bazlı WiX MSI ve taşınabilir ZIP
- Unix izinlerini koruyan ayrı Apple Silicon ve Intel macOS arşivleri
- SHA-256 toplamları, dosya bazlı sürüm manifestleri, paket doğrulama, bağımlılık
  denetimi ve üçüncü taraf bildirim kontrolü
- İsteğe bağlı Windows Authenticode imzası ve GitHub artifact attestation

## İndirme ve kurulum

Yalnızca bu deponun GitHub Releases sayfasındaki dosyaları kullanın. İşletim
sistemi uyarısını kabul etmeden önce yayımlanan checksum değerini doğrulayın.

### Windows MSI

1. [Son sürümden](https://github.com/maliboz/CutLocal/releases/latest)
   `CutLocal-<sürüm>-win-x64-setup.msi` ve `SHA256SUMS.txt` dosyalarını indirin.
2. MSI dosyasını doğrulayın:

   ```powershell
   Get-FileHash .\CutLocal-0.1.5-win-x64-setup.msi -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. MSI dosyasını çalıştırın. Uygulama mevcut kullanıcı için
   `%LOCALAPPDATA%\Programs\CutLocal` altına kurulur; yönetici yetkisi gerekmez.
4. Windows **Bilinmeyen yayıncı** uyarısı gösterirse yalnızca checksum eşleşiyor
   ve sürüm kaynağına güveniyorsanız devam edin. İmzasız yapılar açıkça belirtilir.

Kaldırma işlemi uygulama dosyalarını ve kısayolları siler. Modelleri, ayarları,
günlükleri ve `%LOCALAPPDATA%\CutLocal` altındaki kurtarma durumunu bilerek korur.

### Windows taşınabilir ZIP

`CutLocal-<sürüm>-win-x64-portable.zip` dosyasını indirin, doğrulayın ve
`CutLocal.exe` dosyasını çalıştırmadan önce arşivin tamamını çıkartın. EXE'yi ZIP
içinden çalıştırmayın ve yanındaki yerel çalışma zamanı dosyalarından ayırmayın.

### macOS arşivi

Doğru paketi seçin:

- Apple M serisi işlemci: `CutLocal-<sürüm>-macos-arm64.tar.gz`
- Intel işlemci: `CutLocal-<sürüm>-macos-x64.tar.gz`

Mevcut topluluk paketleri Apple Developer ID ile imzalanmamış ve
noterlenmemiştir. İlk açılış için:

1. `.tar.gz` dosyasına çift tıklayarak paketi tamamen çıkartın.
2. Spotlight ile **Terminal** uygulamasını kendiniz açın. Arşivdeki `.command`
   dosyasına Finder'dan çift tıklamayın.
3. Sonundaki boşluk dâhil `/bin/bash ` yazın.
4. Çıkartılan klasördeki `FIX-CUTLOCAL.command` dosyasını Terminal'e sürükleyin.
5. Enter'a basın. Okunabilir betik yalnızca yanındaki `CutLocal.app` paketini
   doğrular, karantinayı o paketten kaldırır, yerel ad-hoc imza uygular, doğrular
   ve uygulamayı açar.
6. Başarılı açılıştan sonra `CutLocal.app` dosyasını Uygulamalar'a taşıyın.

Betik başarısız olursa Terminal çıktısını saklayın ve kişisel bilgileri
temizleyerek hata bildirimine ekleyin. Güvenmediğiniz bir kaynaktan gelen ilk
açılış betiğini çalıştırmayın.

## Hızlı kullanım

1. Tek görsel veya toplu iş çalışma alanını açın.
2. Bir PNG dosyası seçin ya da pencereye bırakın.
3. Windows GPU davranışını araştırmıyorsanız sağlayıcıyı Otomatik bırakın.
4. Modeli ve çıktı klasörünü seçin.
5. Varsayılan sonuç iyileştirme gerektiriyorsa maske ayarlarını değiştirin.
6. İşlemi başlatın ve özgün boyutlu saydam PNG'yi inceleyin.

### Model giriş boyutu ve çıktı boyutu

U2NetP ile işlenen `1000×1000` bir girdi için:

1. CutLocal geçici bir `320×320` RGB tensörü oluşturur.
2. Model bir ön plan maskesi üretir.
3. Yalnızca maske `1000×1000` boyutuna yeniden ölçeklendirilir.
4. Maske özgün `1000×1000` RGB piksellere uygulanır.

Çıktı `1000×1000` kalır. Daha yüksek çözünürlüklü bir model zor sınırları
iyileştirebilir ama daha fazla bellek kullanır ve kusursuz maske garantisi vermez.

### Başlangıç maske ayarları

- Nötr başlangıç değeri olarak eşiği `0,50` bırakın.
- Saç ve tüy için `0–1 px`, ürün kenarları için `1–2 px` yumuşatmayla başlayın.
- Fotoğraf, saç, tüy, cam ve yumuşak kenarlarda sert kesimi kapalı tutun.
- Sert kesimi yalnızca düz logo gibi bilerek ikili üretilen siluetlerde kullanın.

## Modeller ve lisanslar

Model kodu ile model ağırlıkları farklı koşullara sahip olabilir. CutLocal her
manifestte ihtiyatlı bir politika kaydeder; gerekli alanlar, hash, boyut veya
lisans kararı eksikse güvenli biçimde işlemi reddeder.

| Model | Standart sürüm | Çalışma zamanı indirmesi | Girdi | CutLocal ağırlık politikası |
|---|---|---|---:|---|
| U2NetP Fast | Dâhil | Onarma kullanılabilir | 320×320 | Apache-2.0 kaynak proje; atıf korunur |
| BiRefNet General Lite | Asla paketlenmez | Açık onay gerekir | 1024×1024 | `LicenseRef-BiRefNet-Weights-NonCommercial`; ticari kullanıma politika gereği izin verilmez |
| BRIA RMBG-2.0 | Asla paketlenmez | Açık onay gerekir | 1024×1024 | CC BY-NC 4.0; ticari kullanım ayrı izin gerektirir |

BiRefNet kaynak kodu deposu MIT lisanslıdır; ancak bu durum ayrı dağıtılan bütün
ağırlıkların lisansını otomatik olarak çözmez. İncelenen ağırlıkta yeterince açık,
ağırlığa özel izinli bir lisans bulunmadığı ve kaynak belgede ağırlıklar ticari
olmayan kullanımla tanımlandığı için CutLocal modeli kısıtlı kabul eder. Ayrıntılar:
[lisans analizi](docs/licensing.md), [model manifestleri](assets/models/manifests)
ve [üçüncü taraf bildirimleri](ThirdPartyNotices.txt).

Hiçbir `.onnx` model ağırlığı Git geçmişinde tutulmaz. Standart sürüm paketleri
yalnızca incelenmiş U2NetP varsayılan modelini içerir. İsteğe bağlı kısıtlı
modeller yalnızca kullanıcı işlemi ve tam bütünlük doğrulamasından sonra indirilir.

## Gizlilik ve ağ davranışı

- Görsel içeriği CutLocal'a ait bir yükleme yolu üzerinden uygulama dışına çıkmaz.
- Çıkarım ağ bağlantısı gerektirmez.
- Hesap, reklam kimliği, çökme yükleyicisi veya ürün telemetrisi yoktur.
- Uygulama ağı yalnızca kullanıcı Model Yöneticisi'nde indirme ya da onarma
  işlemini başlattığında kullanır.
- Günlükler yereldir; görsel içeriği veya tam girdi yollarını içermeyecek şekilde
  tasarlanmıştır. Kullanıcılar yine de herkese açık paylaşım öncesinde incelemelidir.
- Ayarlar, kurtarma durumu, modeller ve günlükler yerelde saklanır. Platform
  yolları ve silme davranışı için [gizlilik bildirimine](docs/privacy.md) bakın.

## Bilinen sınırlamalar

- 0.1.5 sürümü 1.0 öncesi bir önizlemedir; mevcut testlerin veya elle
  doğrulamanın gözden kaçırdığı hatalar bulunabilir.
- Kararlı çözme/dışa aktarma hattı şu anda PNG girdi ve saydam PNG çıktı destekler.
  JPEG, WebP, BMP ve TIFF mevcut özellik iddiası değil, yol haritası maddeleridir.
- Segmentasyon kalitesi nesneye, arka plana, modele ve iyileştirme ayarlarına
  bağlıdır. İnce saç, tüy, cam, hareket bulanıklığı, düşük kontrast ve üst üste
  nesneler zor örnekler olmaya devam eder.
- DirectML davranışı Windows GPU sürücüsüne bağlıdır. CPU geri dönüşü uygun GPU
  hatalarında hız yerine işlemin tamamlanmasını önceler.
- macOS paketleri imzasız ve noterlenmemiştir. İlk açılış yöntemi ücretli
  Developer ID dağıtımına göre daha zahmetlidir.
- Yerel macOS arayüz kapsamı ve gerçek donanım matrisi Windows'a göre daha dardır.
  Intel ve alışılmadık donanım/sürücü birleşimleri daha fazla topluluk testi ister.
- Otomatik testler riski azaltır; her girdide hata, bellek gerilemesi, güvenlik
  açığı veya model kalitesi sorunu olmadığını kanıtlayamaz.

Mevcut doğrulama sınırı için
[bilinen sınırlamalar ve bildirim rehberini](docs/known-limitations.md) okuyun.

## Mimari

```text
CutLocal.App (WPF)          CutLocal.Mac (Avalonia)
          \                  /
           CutLocal.Application
                    |
           CutLocal.Contracts
                    |
             CutLocal.Domain
                    |
         CutLocal.Infrastructure
          /        |          \
  Inference     Imaging     Persistence
```

Katmanlar UI kodunu çıkarım ve dosya sistemi uygulama ayrıntılarından ayırır.
Çıkarım hattı manifest tabanlıdır, oturumlar yeniden kullanılır ve önizleme
görselleri özgün çözünürlüklü işlemden bağımsız sınırlandırılır. Ayrıntılar:
[mimari kararlar](docs/architecture.md), [bellek bütçesi](docs/memory-budget.md)
ve [hata/geri dönüş politikası](docs/failure-fallback.md).

## Kaynak koddan derleme

Depo `global.json` içinde .NET 10 SDK sürümüne sabitlenmiştir.

### Windows

```powershell
dotnet restore CutLocal.sln
dotnet format CutLocal.sln --verify-no-changes --no-restore
dotnet build CutLocal.sln --configuration Release --no-restore
dotnet test CutLocal.sln --configuration Release --no-build --no-restore
dotnet run --project src\CutLocal.App\CutLocal.App.csproj --configuration Release
```

Yerel işleme gerekiyorsa sabitlenmiş U2NetP geliştirme modelini kurun:

```powershell
.\tools\Install-DevelopmentModel.ps1
```

### macOS uygulaması

.NET 10 SDK kurulu bir Mac'te:

```bash
dotnet restore src/CutLocal.Mac/CutLocal.Mac.csproj
dotnet run --project src/CutLocal.Mac/CutLocal.Mac.csproj --configuration Release
```

Windows çapraz paketleme betiği iki mimari arşivi de üretir; son imzalama,
noterleme ve açılış testi için yine yerel bir Mac gerekir.

### Sürüm paketleri

```powershell
.\installer\Build-Release.ps1 -Version 0.1.5
.\installer\Build-MacRelease.ps1 -Version 0.1.5
```

Sürüm betikleri yalnızca standart incelenmiş modeli alır, tam boyut ve SHA-256
değerini doğrular, self-contained uygulamaları yayımlar, paket yapısını denetler
ve checksum üretir. Ayrıntılar [sürüm rehberindedir](docs/release.md).

## Testler ve kalite kapıları

Solution; birim, entegrasyon, golden-image, stres, UI render, model ve paketleme
testleri içerir. CI biçimlendirmeyi, warnings-as-errors derlemeyi, test paketlerini,
manifestleri, bağımlılık açıklarını, üçüncü taraf envanterini, arşiv bütünlüğünü
ve MSI yapısını kontrol eder. Bazı gerçek GPU, uzun süre ve yerel macOS kontrolleri
elle veya donanıma bağlıdır; evrensel kapsam varmış gibi sunulmaz.

[Test matrisi](docs/test-matrix.md) ve son
[hazırlık raporu](docs/open-source-readiness-report.md) ayrıntıları içerir.

## Depo düzeni

```text
src/        Üretim uygulamaları ve paylaşılan kütüphaneler
tests/      Birim, entegrasyon, golden-image, stres ve benchmark projeleri
tools/      Model ve macOS arşiv araçları
installer/  Windows/macOS paketleme ve doğrulama betikleri
assets/     Marka varlıkları, model manifestleri ve lisans bildirimleri; ağırlık yok
docs/       Mimari, gizlilik, test, lisans ve sürüm kayıtları
.github/    CI, sürüm otomasyonu, issue formları ve bağımlılık güncellemeleri
```

Yerel SDK'lar, derleme çıktıları, paketler, günlükler, model ağırlıkları,
sertifikalar ve yerel test dosyaları `.gitignore` tarafından hariç tutulur.

## rembg ile ilişki

CutLocal bir rembg fork'u değildir. Bağımsız bir C#/.NET uygulamasıdır ve rembg
Python paketini içermez veya çalıştırmaz. rembg, belgelenmiş davranış için teknik
referans ve sabitlenmiş ONNX dosyaları için dağıtım konumu olarak kullanılmıştır.
MIT atfı [NOTICE](NOTICE) ve [ThirdPartyNotices.txt](ThirdPartyNotices.txt) içinde
korunur.

Yalnızca rembg'nin kendisini değiştirip katkı gönderecekseniz fork kullanın.
CutLocal bağımsız bir depo olarak yayımlanmalıdır.

## Katkı, destek ve güvenlik

Pull request açmadan önce [CONTRIBUTING.md](CONTRIBUTING.md) belgesini okuyun.
Destek sınırları için [SUPPORT.md](SUPPORT.md), güvenlik açıklarını özel bildirmek
için [SECURITY.md](SECURITY.md) kullanılır. Katılım
[Davranış Kuralları](CODE_OF_CONDUCT.md) kapsamındadır.

Proje topluluk tarafından sürdürülür; garanti veya kesin yanıt süresi yoktur.
Hata bildiriminde CutLocal sürümü, işletim sistemi, CPU/GPU, model, sağlayıcı,
girdi boyutları, yeniden üretme adımları ve özel görsel ya da kişisel yol
içermeyen temizlenmiş günlükler bulunmalıdır.

## Lisans

CutLocal'ın özgün kaynak kodu ve belgeleri [Apache License 2.0](LICENSE) ile
lisanslanmıştır; telif hakkı 2026 CutLocal contributors. Apache-2.0, koşullarına
uyulması hâlinde ticari ve ticari olmayan kullanıma izin verir.

Bağımlılıklar ve model ağırlıkları kendi lisansları veya politika kısıtları
altında kalır. CutLocal lisansı bunları yeniden lisanslamaz. Yeniden dağıtımdan
önce [NOTICE](NOTICE), [ThirdPartyNotices.txt](ThirdPartyNotices.txt) ve
[docs/licensing.md](docs/licensing.md) dosyalarını inceleyin.

Yazılım hiçbir garanti veya koşul olmadan **olduğu gibi** sunulur.
