# Geliştirme Notu 0: Geliştirme Ortamı, Git ve GitHub

| | |
|---|---|
| Kapsam | Geliştirme ortamı, Git ve GitHub iş akışı, değişiklik yönetimi, depo güvenliği |
| Sonuç | Kullanıcı klasörüne kurulmuş .NET 10 SDK, kuralları belli bir depo, GitHub'da public repo |
| Depo | https://github.com/sefatuncer/dijital-kimlik-oid4vc |

## 1. Ne yaptık, neden?

Kod yazmadan önce üç şeyi sabitledik:

1. **Her makinede aynı SDK.** SDK, uygulamayı derlemeye yarayan araç setidir (ayrıntı 2. kısımda).
   `global.json` dosyası hangi SDK sürümünün kullanılacağını yazar. Böylece benim makinemde, CI'da
   ve başka bir geliştiricinin bilgisayarında aynı derleyici çalışır. "Bende çalışıyordu" sorunu
   baştan önlenir.
2. **Ortak kalite kuralları.** `Directory.Build.props` dosyası, klasördeki bütün projelere aynı
   derleme ayarlarını verir. Uyarılar hata sayılır: uyarı veren kod derlenmez. Buna **kalite
   kapısı** (quality gate) denir. Kalite kapısı, belli bir kontrolden geçmeyen kodun bir sonraki
   adıma ilerleyememesidir.
3. **İzlenebilir değişiklik.** Her değişiklik Git'te bir commit olur. Commit, kim neyi ne zaman
   değiştirdi bilgisini taşıyan kayıttır. Değişiklik `main` dalına girmeden önce bir **PR**'dan
   (pull request) geçer. PR, "bu dalı ana dala almak istiyorum, inceleyin" isteğidir. Orada kod
   okunur ve otomatik testler koşar. İlandaki "değişiklik yönetimi" maddesinin uygulamadaki
   karşılığı budur.

## 2. Kavramlar

**SDK ile runtime farkı.**

- **Runtime** (çalışma zamanı) uygulamayı *çalıştırır*. İçinde çöp toplayıcı (GC) ve temel
  kütüphaneler vardır.
- **SDK** (Software Development Kit) uygulamayı *geliştirmeye* yarar. İçinde şunlar bulunur:
  - **Roslyn:** C# derleyicisi. `.cs` dosyalarını DLL'e çevirir.
  - **MSBuild:** Microsoft'un derleme motoru. `.csproj` dosyasını okur ve derleme adımlarını
    sırayla çalıştırır.
  - **`dotnet` komutları:** `new`, `build`, `test`, `publish`.
  - **NuGet istemcisi:** .NET'in paket yöneticisi. Dış kütüphaneleri indirir.
- Sunucuya yalnız runtime kurulur. Web uygulaması için ASP.NET Core Runtime kurulur. Uygulama
  IIS'te çalışacaksa onun yerine **Hosting Bundle** kurulur. Bundle, runtime'ı ve **ANCM**'yi
  (ASP.NET Core Module) birlikte kurar. ANCM, IIS'e gelen isteği .NET uygulamasına ileten IIS
  modülüdür. Geliştirici makinesine SDK kurulur.

**.NET sürüm politikası.** Her yıl kasımda yeni bir ana sürüm çıkar.

- Çift numaralı sürümler **LTS**'tir (Long Term Support). 3 yıl güvenlik yaması alırlar.
- Tek numaralılar **STS**'dir (Standard Term Support). Desteği daha kısadır: 2 yıl.
- .NET 10 bir LTS sürümüdür. Kamu kurumları genellikle LTS'yi seçer, çünkü sürüm yükseltmek onay,
  test ve kurulum gerektirir ve bunu sık yapamazlar.

**`global.json`.** `dotnet` komutu hangi SDK'yı kullanacağını bu dosyadan öğrenir. Dosyada iki
değer var:

- `"version": "10.0.402"`: en az bu SDK gerekir.
- `"rollForward": "latestFeature"`: aynı ana sürümde (10.0) kurulu olan en yeni **özellik
  bandı** seçilir. Özellik bandı, SDK sürüm numarasının yüzler basamağıdır: 10.0.1xx, 10.0.2xx,
  10.0.4xx gibi. Yeni bantlar yeni SDK özellikleri getirir, runtime aynı kalır.

Örnek: makinede 10.0.402 ve 10.0.500 kuruluysa 10.0.500 seçilir. Yalnız 10.0.300 kuruluysa derleme
"uygun SDK bulunamadı" hatası verir. .NET 11 SDK'sı kurulu olsa bile seçilmez. Sonuç: herkes en az
aynı SDK'yla derler, ama küçük bir SDK yükseltmesi derlemeyi kırmaz.

**Merkezi paket yönetimi (Central Package Management, CPM).** Normalde her proje kullandığı NuGet
paketinin sürümünü kendi `.csproj` dosyasına yazar. On proje varsa aynı paketin on farklı sürümü
olabilir. Buna "sürüm kayması" denir. CPM'de sürümler tek bir dosyada, `Directory.Packages.props`'ta
tutulur. Projeler yalnız paketin adını yazar. Ayar: `ManagePackageVersionsCentrally=true`. Ne işe
yarar: bir güvenlik açığı çıktığında paket tek satırla, bütün projelerde birden güncellenir.

**`.gitattributes` ve satır sonları.** Metin dosyasında her satırın sonunda görünmez bir karakter
vardır. Windows iki karakter kullanır (CRLF: `\r\n`), Linux bir karakter kullanır (LF: `\n`).

- `* text=auto` kuralı, depoda her zaman LF saklanmasını sağlar. Dosya çekildiğinde (checkout)
  işletim sistemine göre çevrilir.
- `.sh` dosyaları her zaman LF olmalıdır. Linux'ta satır sonundaki `\r`, komutun bir parçası
  sanılır ve betik bozulur.
- `.ps1` ve `.cmd` dosyaları çalışma kopyasında her zaman CRLF olur (`eol=crlf`). Bunlar Windows
  betikleridir ve Windows araçlarıyla uyumlu kalmaları istenir.

## 3. Adım adım

### 3.1 SDK'nın yönetici yetkisi olmadan kurulması

Durum: makinede `C:\Program Files\dotnet` altında yalnız runtime vardı, SDK yoktu. Bunu
`dotnet --info` komutuyla gördük. Bu komut kurulu SDK ve runtime'ları listeler:

```text
> dotnet --info
.NET SDKs installed:
  No SDKs were found.
.NET runtimes installed:
  Microsoft.NETCore.App 8.0.14, 10.0.8, 10.0.10 ...
```

Microsoft'un resmî kurulum betiğini indirdik. **Çalıştırmadan önce imzasını doğruladık.**
Authenticode imzası, dosyanın kimin tarafından yayımlandığını ve yolda değiştirilmediğini gösterir.
İnternetten indirilen bir betik, imzası doğrulanmadan çalıştırılmamalıdır.

```powershell
# 1. Betiği indir
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
# 2. İmzayı denetle. Beklenen: Status = Valid, imzalayan = CN=Microsoft Corporation
Get-AuthenticodeSignature .\dotnet-install.ps1
# 3. SDK'yı kullanıcı klasörüne kur
#    -Channel 10.0   : .NET 10'un en güncel SDK'sı
#    -InstallDir     : kurulacak klasör. Kullanıcı klasörü olduğu için yönetici yetkisi gerekmez
#    -NoPath         : PATH ortam değişkenini kalıcı olarak değiştirme
.\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:USERPROFILE\.dotnet" -NoPath
```

Sonuç: `10.0.402 [C:\Users\tuncer\.dotnet\sdk]`, 770 MB. PATH değişmediği için öteki programlar
etkilenmez. Kaldırmak için klasörü silmek yeterlidir.

### 3.2 Oturum betiği

PATH değişmediği için `dotnet` komutu yeni SDK'yı kendiliğinden bulamaz. `scripts/ortam.ps1` betiği
SDK'yı yalnız açık olan PowerShell penceresinde PATH'e ekler ve telemetriyi kapatır. Telemetri,
SDK'nın kullanım verisini Microsoft'a göndermesidir.

```powershell
. .\scripts\ortam.ps1      # baştaki ". " (dot-source), betiğin bu oturumu değiştirmesini sağlar
SDK: 10.0.402  (C:\Users\tuncer\.dotnet)
```

Baştaki nokta önemlidir. Nokta olmadan betik ayrı bir kapsamda çalışır ve yaptığı PATH değişikliği
betik bitince kaybolur.

### 3.3 Windows Uygulama Denetimi testi

**Windows App Control** (eski adıyla WDAC), kurumların hangi programların ve DLL'lerin
çalışabileceğini belirlediği bir Windows güvenlik özelliğidir. Bu makinede bazı yerel (native)
DLL'leri engelliyor. Proje kurulmadan önce küçük bir konsol uygulamasıyla denedik:

```powershell
dotnet new console -o appcontrol-test     # "Hello, World!" yazan boş uygulama oluşturur
dotnet run --project appcontrol-test      # derler ve çalıştırır
```

Sonuç: `Hello, World!`, çıkış kodu 0. Derlenen yönetilen (managed) DLL'ler o gün engellenmedi.
Yönetilen DLL, .NET'in çalıştırdığı ara koddur (IL). Yerel DLL ise doğrudan işlemcinin çalıştırdığı
makine kodudur.

**Sonradan değişti (Faz 3b):** Yeni derlenen ölçüm aracının DLL'i engellendi (hata `0x800711C7`).
Bu yüzden ölçüm aracı Linux SDK konteynerinde çalıştırıldı. Ders: App Control kararı dosyaya göre
değişebilir. Kurumda bu engelle karşılaşınca kural istisnası güvenlik birimiyle konuşulur ya da
araç konteynerde çalıştırılır.

### 3.4 Depo ve kurallar

```powershell
dotnet new gitignore      # .NET için hazır .gitignore: bin/, obj/ gibi derleme çıktıları depoya girmez
dotnet new editorconfig   # kod stili kuralları dosyası
git init -b main          # Git deposu oluşturur, ana dalın adı main
```

| Dosya | Görevi |
|---|---|
| `global.json` | SDK 10.0.402'yi sabitler |
| `Directory.Build.props` | Bütün projelere ortak ayarlar. `Nullable` derleyicinin null hatalarını yakalamasını sağlar. `ImplicitUsings` sık `using` satırlarını otomatik ekler. `TreatWarningsAsErrors` uyarıyı hata sayar. `EnforceCodeStyleInBuild` stil kurallarını derlemede denetler. `AnalysisLevel=latest` en güncel kod analizcilerini açar |
| `Directory.Packages.props` | Merkezi paket sürümleri (CPM) |
| `.editorconfig` | Girinti, isimlendirme ve stil kuralları. IDE ve derleme aynı kuralları uygular |
| `.gitattributes` | Satır sonu kuralları |
| `.gitignore` | Depoya girmeyecek dosyalar: `bin/`, `obj/`, yerel gizli ayarlar, `publish/`, PDF çıktıları |

### 3.5 Commit kimliği ve gizlilik

Her commit, yazarının adını ve e-posta adresini taşır. Public bir depoda bu e-posta herkese açıktır
ve `git log` ile görülebilir. Bu makinede global Git ayarında kişisel adres tanımlıydı. Bu depoya
özel olarak GitHub'ın **noreply** adresini verdik. Noreply adresi, GitHub'ın her hesaba verdiği ve
gerçek posta kutusuna gitmeyen bir adrestir. Commit'ler yine hesaba bağlanır ama kişisel adres
görünmez.

```powershell
$id = gh api user --jq .id     # GitHub hesabının sayısal kimliğini alır
git config user.email "$id+sefatuncer@users.noreply.github.com"   # --global yok: yalnız bu depo
```

Global ayara dokunulmadı. Commit mesajları **Conventional Commits** biçiminde yazılır. Bu, mesajın
başına değişikliğin türünü yazma kuralıdır:

| Önek | Anlamı |
|---|---|
| `feat:` | Yeni özellik |
| `fix:` | Hata düzeltmesi |
| `docs:` | Yalnız doküman |
| `test:` | Yalnız test |
| `refactor:` | Davranışı değiştirmeyen kod düzenlemesi |
| `chore:` | Bakım işi (paket güncellemesi, ayar) |
| `perf:` | Performans iyileştirmesi |

Ne işe yarar: geçmişte "hangi commit hata düzeltmesiydi?" sorusu kolayca cevaplanır. Sürüm notu da
bu öneklerden otomatik üretilebilir.

### 3.6 GitHub reposu ve güvenlik ayarları

```powershell
# Public repo oluştur, yerel depoyu "origin" adıyla bağla ve ilk push'u yap
gh repo create sefatuncer/dijital-kimlik-oid4vc --public --source . --remote origin --push
# Dependabot uyarılarını aç: kullanılan pakette güvenlik açığı çıkarsa GitHub haber verir
gh api -X PUT repos/sefatuncer/dijital-kimlik-oid4vc/vulnerability-alerts
# Otomatik güvenlik PR'larını aç: Dependabot açığı kapatan sürüm için kendisi PR açar
gh api -X PUT repos/sefatuncer/dijital-kimlik-oid4vc/automated-security-fixes
```

Ayrıca iki ayar daha açıldı:

- **Secret scanning:** GitHub, depodaki dosyalarda parola, API anahtarı ve token arar.
- **Push protection:** Bir anahtar ya da parola yanlışlıkla commit'lenmişse GitHub o push'u
  reddeder. Sır daha GitHub'a ulaşmadan durdurulur.

Merge yöntemi ilk başta yalnız *squash* olarak ayarlandı: her PR `main`'de tek commit olur.
Birleşen dallar otomatik siliniyordu.

**Branch protection** (dal koruması), bir dala kimin nasıl yazabileceğini belirleyen kurallardır.
İlk gün açılamadı. Zorunlu tutulacak kontrolün adı ancak CI bir kez çalıştıktan sonra listeden
seçilebiliyor. Bu yüzden Faz 1'de, CI eklendikten sonra açıldı.

**Sonradan değişti (Faz 2):** GitHub web arayüzündeki birleştirme düğmesi, squash da rebase de,
yeni commit'in committer alanına hesabın birincil e-postasını yazıyor. Yani kişisel adres yine
görünüyor. `gh pr merge --author-email` komutu ise noreply adresini kabul etmedi. Bu yüzden akış
değişti:

1. Faz dalı push edilir.
2. PR açılır ve CI'ın yeşil olması beklenir.
3. Dal, yerel bilgisayardan `main`'e **fast-forward** edilir: `git push origin <dal>:main`.
   Fast-forward, yeni bir birleştirme commit'i oluşturmadan `main`'i dalın son commit'ine ilerletmektir.
   Commit'ler olduğu gibi kalır, yazar ve committer noreply adresi olur.

Branch protection şimdi şunları zorunlu tutuyor:

- "Derleme ve test" kontrolü geçmeli. *Strict* açık: dal `main`'in en son hâlinin üzerine kurulmuş
  olmalı. Böylece test edilen kod ile `main`'e giren kod aynı olur.
- Geçmiş doğrusal olmalı: birleştirme commit'i yok.
- Force push ve dal silme kapalı. Force push, uzaktaki geçmişi silip yerine başkasını yazmaktır.
- Kurallar yöneticiye de uygulanıyor (*enforce_admins*). Depo sahibi de kuralı atlayamaz.

PR zorunluluğu kaldırıldı, çünkü fast-forward push'u engelliyordu. Ama her faz yine bir PR ile
incelenir ve CI orada çalışır.

## 4. Çıkan sorunlar ve çözümleri

| # | Belirti | Neden | Çözüm |
|---|---|---|---|
| 1 | `pdftotext`: *Couldn't open file 'G<f6><e7>-<dd>daresi...'* | Git Bash'teki araç, Türkçe karakterli Windows dosya adını yanlış kod sayfasıyla (karakter tablosuyla) okudu | Dosyayı ASCII bir adla kopyalayıp öyle okuduk. Proje dosya adlarında Türkçe karakter kullanmıyoruz |
| 2 | `dotnet build`: SDK bulunamadı | Makinede yalnız runtime vardı | SDK kullanıcı klasörüne kuruldu, `ortam.ps1` ile PATH ayarlandı |
| 3 | E-posta ifşası riski | Global Git e-postası kişisel adresti | Depoya özel noreply adresi |
| 4 | `warning: CRLF will be replaced by LF` | Windows'ta oluşan dosyalar CRLF, `.gitattributes` ise depoda LF istiyor | Hata değil, uyarı. Git dosyayı depoya LF olarak yazıyor, yani kural çalışıyor. `.ps1` için ters yöndeki uyarı da kurala uygun |


## 5. Terimler sözlüğü

| Terim | Ne demek? | Ne işe yarar, nerede geçer? |
|---|---|---|
| ANCM | ASP.NET Core Module, bir IIS modülü | IIS'e gelen isteği .NET uygulamasına iletir; 2 |
| App Control (WDAC) | Hangi program ve DLL'lerin çalışacağını belirleyen Windows güvenliği | Bu makinede bazı DLL'leri engelliyor; 3.3 |
| Authenticode imzası | Dosyanın yayımcısını ve bozulmadığını gösteren dijital imza | İndirilen betiği çalıştırmadan önce doğrulamak; 3.1 |
| Branch protection | Bir dala yazma kuralları | `main`'e test geçmeden kod girmesini önler; 3.6 |
| Commit | Bir değişikliğin kim, ne, ne zaman bilgisiyle kaydı | Git geçmişinin birimi; 1 |
| Conventional Commits | Commit mesajının başına tür yazma kuralı (`feat:`, `fix:`) | Geçmişi okunur kılar, sürüm notu üretir; 3.5 |
| CPM | Central Package Management, merkezi paket yönetimi | Paket sürümlerini tek dosyada tutar; 2 |
| CRLF / LF | Windows ve Linux'un satır sonu karakterleri | `.gitattributes` ile denetlenir; 2 |
| Dot-source (`. .\betik.ps1`) | Betiği mevcut oturumda çalıştırmak | Betiğin PATH değişikliği oturumda kalır; 3.2 |
| Enforce admins | Dal kurallarının yöneticiye de uygulanması | Depo sahibi de kuralı atlayamaz; 3.6 |
| Fast-forward | Yeni commit oluşturmadan dalı ileri almak | Bu projede `main`'e giriş yolu; 3.6 |
| Force push | Uzaktaki geçmişi silip yeniden yazmak | `main`'de kapalı; 3.6 |
| `global.json` | Kullanılacak SDK sürümünü yazan dosya | Her makinede aynı derleyici; 2 |
| Hosting Bundle | Runtime + ANCM kurulum paketi | IIS sunucusuna .NET uygulaması kurmak; 2 |
| Kalite kapısı (quality gate) | Kontrolden geçmeyen kodun ilerleyememesi | Uyarı = hata, CI zorunlu kontrolü; 1 |
| LTS / STS | Uzun (3 yıl) ve standart (2 yıl) destekli .NET sürümleri | Kurumlar LTS seçer; 2 |
| MSBuild | Microsoft'un derleme motoru | `.csproj`'yi okuyup derler; 2 |
| Noreply e-posta | GitHub'ın verdiği, posta almayan adres | Kişisel e-postayı commit'te gizler; 3.5 |
| NuGet | .NET paket yöneticisi | Dış kütüphaneleri indirir; 2 |
| PR (pull request) | Dalı ana dala alma ve inceleme isteği | Kod incelemesi ve CI burada; 1 |
| Push protection | Sır içeren push'u reddeden GitHub özelliği | Sızıntıyı GitHub'a ulaşmadan durdurur; 3.6 |
| Rebase | Commit'leri hedef dalın ucuna yeniden yazmak | Doğrusal geçmiş; 3.6 |
| Roslyn | C# derleyicisi | `.cs` dosyalarını DLL'e çevirir; 2 |
| Runtime | Uygulamayı çalıştıran ortam | Sunucuya kurulur; 2 |
| SDK | Geliştirme araç seti | Geliştirici makinesine kurulur; 2 |
| Secret scanning | Depoda sır arayan GitHub özelliği | Sızmış anahtarı bildirir; 3.6 |
| Squash | Dalın commit'lerini tek commit'e indirmek | Okunaklı `main` geçmişi; 3.6 |
| Strict (status check) | Dalın `main`'in son hâline göre güncel olma şartı | Test edilen kod = giren kod; 3.6 |
| `TreatWarningsAsErrors` | Uyarıyı derleme hatası saymak | Kalite kapısı; 3.4 |
| Yönetilen / yerel DLL | IL kodu / makine kodu içeren DLL | App Control farklı davranabilir; 3.3 |
