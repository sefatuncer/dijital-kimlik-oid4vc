# Bölüm 0: Geliştirme Ortamı, Git ve GitHub

| | |
|---|---|
| İlan maddeleri | **l** (TFS, Azure DevOps, Git, SVN), **b** (kalite, değişiklik yönetimi), **c** (güvenli yazılım geliştirme) |
| Sonuç | Kullanıcı klasörüne kurulmuş .NET 10 SDK, kuralları belli bir depo, GitHub'da public repo |
| Depo | https://github.com/sefatuncer/dijital-kimlik-oid4vc |

## 1. Bu bölümde ne yaptık, neden?

Kod yazmadan önce üç şeyi sabitledik:

1. **Her makinede aynı SDK.** `global.json` SDK sürümünü sabitler. Benim makinemde, CI'da ya da
   sınavdaki bilgisayarda aynı derleyici çalışır.
2. **Ortak kalite kuralları.** `Directory.Build.props` tüm projelere aynı derleme ayarlarını verir.
   Uyarılar hata sayılır; kalite kapısı ilk günden kurulur.
3. **İzlenebilir değişiklik.** Her değişiklik Git'te bir commit olur ve GitHub'da bir PR'dan geçer.
   Bu, ilandaki "değişiklik yönetimi" maddesinin uygulamadaki karşılığı.

## 2. Kavramlar

**SDK ile runtime farkı.** Runtime uygulamayı *çalıştırır*. SDK ise derleyiciyi (Roslyn), MSBuild'i,
`dotnet new/build/test/publish` komutlarını ve NuGet istemcisini içerir; yani uygulamayı
*geliştirmeye* yarar. Sunucuya yalnız runtime (web için ASP.NET Core Runtime veya IIS'te Hosting
Bundle) kurulur. Geliştirici makinesine SDK kurulur.

**.NET sürüm politikası.** Her yıl kasımda yeni bir ana sürüm çıkar. Çift numaralı sürümler
**LTS**'tir (3 yıl destek), tek numaralılar **STS**'dir (daha kısa destek). .NET 10 LTS. Kamu kurumlarında genellikle LTS tercih edilir, çünkü
güncelleme döngüsü yavaştır.

**`global.json`.** `dotnet` komutu hangi SDK'yı kullanacağını buradan seçer. `rollForward:
latestFeature` aynı ana sürümde daha yeni bir özellik bandına (10.0.5xx gibi) izin verir. Daha
eski bir SDK'ya ya da yeni bir ana sürüme geçişe izin vermez.

**Merkezi paket yönetimi (CPM).** `Directory.Packages.props` içinde
`ManagePackageVersionsCentrally=true` olduğunda NuGet sürümleri tek dosyada tutulur. On projede
on farklı `Newtonsoft.Json` sürümü olması gibi "sürüm kayması" önlenir. Güvenlik yaması tek
satırda yapılır.

**`.gitattributes` ve satır sonları.** Windows CRLF, Linux LF kullanır. `* text=auto` ile depoda
her zaman LF saklanır; checkout sırasında işletim sistemine göre çevrilir. `.sh` dosyaları her
zaman LF olmalıdır (Linux CI'da `\r` yüzünden betik bozulur). `.ps1` dosyaları CRLF kalır.

## 3. Adım adım

### 3.1 SDK'nın yönetici yetkisi olmadan kurulması

Makinede `C:\Program Files\dotnet` altında yalnız runtime vardı:

```text
> dotnet --info
.NET SDKs installed:
  No SDKs were found.
.NET runtimes installed:
  Microsoft.NETCore.App 8.0.14, 10.0.8, 10.0.10 ...
```

Microsoft'un resmî kurulum betiğini indirdik ve **çalıştırmadan önce imzasını doğruladık**.
İnternetten indirilen betik çalıştırılmadan önce doğrulanmalıdır:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
Get-AuthenticodeSignature .\dotnet-install.ps1   # Status: Valid, CN=Microsoft Corporation
.\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:USERPROFILE\.dotnet" -NoPath
```

Sonuç: `10.0.402 [C:\Users\tuncer\.dotnet\sdk]`, 770 MB. `-NoPath` PATH'i kalıcı olarak
değiştirmez. Kaldırmak için klasörü silmek yeterli.

### 3.2 Oturum betiği

`scripts/ortam.ps1`, SDK'yı yalnız açık PowerShell oturumunda etkinleştirir ve telemetriyi kapatır:

```powershell
. .\scripts\ortam.ps1      # baştaki ". " betiğin mevcut oturumu değiştirmesini sağlar
SDK: 10.0.402  (C:\Users\tuncer\.dotnet)
```

### 3.3 Windows Uygulama Denetimi testi

Bu makinede App Control, bazı yerel DLL'leri engelliyor. Proje kurulmadan önce küçük bir konsol
uygulamasıyla test ettik:

```powershell
dotnet new console -o appcontrol-test
dotnet run --project appcontrol-test    # Hello, World!  exit: 0
```

Derlenen yönetilen (managed) DLL'ler engellenmedi. Uygulama doğrudan Windows'ta çalışabilir.
Windows servisi (Faz 11) ve IIS (Faz 18) için bu önemliydi.

### 3.4 Depo ve kurallar

```powershell
dotnet new gitignore      # .NET için hazır .gitignore
dotnet new editorconfig   # kod stili kuralları
git init -b main
```

| Dosya | Görevi |
|---|---|
| `global.json` | SDK 10.0.402'yi sabitler |
| `Directory.Build.props` | Nullable, ImplicitUsings, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest` |
| `Directory.Packages.props` | Merkezi paket sürümleri |
| `.editorconfig` | Girinti, isimlendirme ve stil kuralları; IDE ve derleme aynı kuralları uygular |
| `.gitattributes` | Satır sonu kuralları |
| `.gitignore` | `bin/`, `obj/`, yerel gizli ayarlar, `publish/`, PDF çıktıları |

### 3.5 Commit kimliği ve gizlilik

Commit'teki yazar e-postası GitHub'da herkese açıktır ve `git log` ile görülebilir. Global ayarda
kişisel adres tanımlıydı. Bu depoya özel olarak GitHub'ın *noreply* adresini verdik:

```powershell
$id = gh api user --jq .id
git config user.email "$id+sefatuncer@users.noreply.github.com"   # yalnız bu depo için
```

Global ayara dokunulmadı. Commit mesajları **Conventional Commits** biçiminde:
`feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.

### 3.6 GitHub reposu ve güvenlik ayarları

```powershell
gh repo create sefatuncer/dijital-kimlik-oid4vc --public --source . --remote origin --push
gh api -X PUT repos/sefatuncer/dijital-kimlik-oid4vc/vulnerability-alerts       # Dependabot uyarıları
gh api -X PUT repos/sefatuncer/dijital-kimlik-oid4vc/automated-security-fixes   # otomatik güvenlik PR'ları
```

Ayrıca **secret scanning** ve **push protection** açıldı. Bir anahtar ya da parola yanlışlıkla
commit'lenirse GitHub push'u reddeder. Merge yöntemi ilk başta yalnız *squash* olarak ayarlandı
(her PR `main`'de tek commit olur) ve birleşen dallar otomatik siliniyor.

Branch protection (PR zorunlu, CI geçmeden merge yok) Faz 1'de CI eklendikten sonra açılacak.
Zorunlu kontrolün adı ancak CI bir kez çalıştıktan sonra seçilebiliyor.

**Sonradan değişti (Faz 2):** GitHub'ın web birleştirmesi, squash da rebase de, committer alanına
hesabın birincil e-postasını yazıyor. `gh pr merge --author-email` ise noreply adresini kabul
etmedi. Bu yüzden akış şöyle oldu:

1. Faz dalı push edilir.
2. PR açılır ve CI'ın yeşil olması beklenir.
3. Dal `main`'e yerelden fast-forward edilir: `git push origin <dal>:main`.

Branch protection şimdi şunları zorunlu tutuyor:

- "Derleme ve test" kontrolü geçmeli (strict: dal `main`'in ucunda olmalı).
- Geçmiş doğrusal olmalı.
- Force push ve dal silme kapalı.
- Kurallar yöneticiye de uygulanıyor.

PR zorunluluğu kaldırıldı, ama her faz yine PR ile incelenir.

## 4. Çıkan sorunlar ve çözümleri

| # | Belirti | Neden | Çözüm |
|---|---|---|---|
| 1 | `pdftotext`: *Couldn't open file 'G<f6><e7>-<dd>daresi...'* | Git Bash'teki araç, Türkçe karakterli Windows dosya adını yanlış kod sayfasıyla çözdü | Dosyayı ASCII bir adla kopyalayıp öyle okuduk. Proje dosya adlarında Türkçe karakter kullanmıyoruz |
| 2 | `dotnet build`: SDK bulunamadı | Makinede yalnız runtime vardı | SDK kullanıcı klasörüne kuruldu, `ortam.ps1` ile PATH ayarlandı |
| 3 | E-posta ifşası riski | Global git e-postası kişisel adresti | Depoya özel noreply adresi |
| 4 | `warning: CRLF will be replaced by LF` | Windows'ta oluşan dosyalar CRLF, `.gitattributes` ise depoda LF istiyor | Uyarı beklenen davranış: Git dosyayı depoya LF olarak yazıyor. `.ps1` için ters yönde (LF→CRLF) uyarı da kurala uygun |

## 5. Mülakat soruları ve örnek cevaplar

**S: Git ile SVN arasındaki temel fark nedir?**
C: SVN merkezîdir: tek bir sunucu deposu vardır, commit için sunucuya bağlanmak gerekir, geçmiş
sunucudadır. Git dağıtıktır: her klonda tüm geçmiş bulunur, commit yerelde atılır, `push` ile
paylaşılır. Git'te dal açmak ucuzdur (yalnızca bir işaretçidir). SVN'de dal, dizin kopyasıdır.
SVN'in güçlü yanları: büyük ikili dosyalar, dizin bazlı yetkilendirme, basit doğrusal geçmiş.

**S: TFS ile Azure DevOps arasındaki ilişki nedir?**
C: TFS, Microsoft'un kurum içine kurulan ALM sunucusuydu. 2019'dan itibaren adı **Azure DevOps
Server** oldu; bulut sürümü Azure DevOps Services. İçinde Repos (Git ya da TFVC), Boards (iş
takibi), Pipelines (CI/CD), Test Plans ve Artifacts (paket deposu) bulunur. TFVC, SVN gibi merkezî
bir sürüm kontrol sistemidir. Yeni projelerde Git önerilir. Bu projede GitHub kullanıyoruz:
GitHub Actions, Azure Pipelines'ın, GitHub Issues/Projects de Boards'un karşılığı.

**S: Hangi dal stratejisini kullanırsınız?**
C: İki yaygın model var. **GitFlow**: `main`, `develop`, `feature/*`, `release/*`, `hotfix/*`.
Sürüm takvimi belirli, birden fazla sürümün bakımı süren ürünlerde uygundur. **Trunk-based**:
kısa ömürlü feature dalları, günde birkaç kez `main`'e merge, bitmemiş özellikler feature flag
arkasında. Sürekli teslimatta uygundur. Bu projede trunk-based ve PR zorunluluğu kullanıyoruz.

**S: merge, rebase ve squash farkı nedir?**
C: *Merge* iki geçmişi bir merge commit'iyle birleştirir ve geçmişi korur. *Rebase* dalın
commit'lerini hedefin ucuna yeniden yazar; geçmiş doğrusal olur. Paylaşılmış dalda rebase
yapılmaz, çünkü başkalarının geçmişi bozulur. *Squash* dalın tüm commit'lerini tek commit'e
indirir; `main` geçmişi her PR için tek ve okunaklı bir satır olur.

**S: Değişiklik yönetimi süreciniz nasıl işler?**
C: Değişiklik bir talep (issue/RFC) olarak başlar. Etki ve risk analizi yapılır: hangi
servisler, veritabanı, geriye dönük uyumluluk. Değişiklik ayrı bir dalda geliştirilir ve PR
açılır. PR'da kod incelemesi, otomatik derleme, test ve güvenlik taraması yapılır. Onaylanınca
`main`'e alınır ve sürüm etiketi (semver) verilir. Üretime geçiş onaylı bir ortam adımıyla
(kurumlarda CAB, yani değişiklik danışma kurulu onayı) ve bir geri dönüş planıyla yapılır.
Her adım iz bırakır: kim, neyi, ne zaman, neden değiştirdi.

**S: Semantik sürümleme (semver) nedir?**
C: `MAJOR.MINOR.PATCH`. Geriye uyumsuz API değişikliği MAJOR'ı, geriye uyumlu yeni özellik
MINOR'ı, hata düzeltmesi PATCH'i artırır. Örnek: `2.3.1` sürümünden sonra yeni bir uç nokta
eklenirse sürüm `2.4.0` olur.

**S: `TreatWarningsAsErrors` neden açık?**
C: Uyarılar birikince kimse okumaz; gerçek bir hata (örneğin nullable referans uyarısı, yani
olası `NullReferenceException`) gürültüde kaybolur. Uyarıyı hata saymak kalite kapısını derleme
anına taşır. Bilinçli istisnalar `.editorconfig`'te kural bazında gerekçesiyle kapatılır.

**S: Bir parola yanlışlıkla commit'lenirse ne yaparsınız?**
C: Önce sır **geçersiz kılınır** (parola değiştirilir, anahtar iptal edilir). Geçmişi
temizlemek tek başına yetmez, çünkü sır klonlara ve önbelleklere gitmiş olabilir. Sonra
geçmiş temizlenir (`git filter-repo`) ve olay kaydedilir. Önleme için secret scanning ile push
protection açılır; sırlar kodda değil, user-secrets'ta, ortam değişkeninde ya da bir kasada
(Key Vault, HashiCorp Vault) tutulur.

**S: Runtime ile SDK arasındaki fark nedir? Sunucuya ne kurulur?**
C: SDK geliştirme içindir: derleyici, MSBuild, CLI. Runtime yalnız çalıştırma içindir. Web
sunucusuna ASP.NET Core Runtime kurulur; IIS'te çalışacaksa **Hosting Bundle** kurulur. Bundle
runtime'ı ve ASP.NET Core Module'ü (ANCM) içerir. Alternatif *self-contained publish*:
runtime uygulamayla birlikte paketlenir ve sunucuya hiçbir şey kurulmaz.
