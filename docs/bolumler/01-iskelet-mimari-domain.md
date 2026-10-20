# Bölüm 1: Çözüm İskeleti, Çok Katmanlı Mimari ve Domain Modeli

| | |
|---|---|
| İlan maddeleri | **d** (çok katmanlı mimari), **e** (.NET Core), **m** (Design Pattern, Clean Code, SOLID), **b** (kalite güvencesi), tercihen **ii** (unit test) |
| Sonuç | 11 projelik çözüm, iş kurallarını taşıyan Domain, ilk use-case, 35 test, GitHub Actions CI, korumalı `main` dalı |
| PR | https://github.com/sefatuncer/dijital-kimlik-oid4vc/pull/1 |

## 1. Amaç

Bu bölümde uygulamanın omurgasını kurduk: hangi katman ne yapar, kim kime bağımlı olabilir,
iş kuralları nerede yaşar. Bu kararlar sonradan değiştirmesi en pahalı kararlardır. Mülakatta
"çok katmanlı mimariyi anlatın" sorusunun cevabı da bu bölümdeki yapıdır.

## 2. Mimari

### 2.1 Katmanlar ve bağımlılık yönü

```text
            ┌────────────────────────────────────────────┐
            │  Sunum: Issuer, Wallet, Verifier (MVC+API) │
            │  Worker (Windows servisi), LegacySoap      │
            └───────────────┬───────────────┬────────────┘
                            │               │ (yalnız DI kaydı için)
                            ▼               ▼
            ┌───────────────────────┐   ┌──────────────────────────┐
            │ Application           │◄──│ Infrastructure           │
            │ use-case, arayüzler   │   │ EF Core, ADO.NET, Redis  │
            └───────┬───────────┬───┘   └──────────────────────────┘
                    ▼           ▼
            ┌────────────┐ ┌────────────┐
            │ Domain     │ │ Crypto     │
            │ iş kuralı  │ │ SD-JWT,JWS │
            └────────────┘ └────────────┘
```

Kural: **oklar içe doğru**. Domain hiçbir projeye bağlı değil. Application, Domain'e bağlı.
Infrastructure, Application'daki arayüzleri *uygular*. Bu yüzden ok Infrastructure'dan
Application'a gidiyor, tersi değil. Buna **bağımlılığın tersine çevrilmesi** (Dependency
Inversion) denir. Veritabanını SQL Server'dan PostgreSQL'e çevirmek yalnız Infrastructure'ı etkiler.

### 2.2 Klasik N-katmanlı mimari ile farkı

Klasik 3 katmanda (Sunum → İş → Veri Erişimi) iş katmanı veri katmanına bağlıdır. İş kuralları
dolaylı olarak veritabanı teknolojisine bağlanır, unit test için veritabanı gerekir. Burada
kullandığımız yapı **Clean Architecture / Onion** ailesindendir: iş kuralları merkezde,
teknoloji dış halkada. İkisi de "çok katmanlı"dır. Fark, bağımlılığın yönündedir.

### 2.3 Projeler

| Proje | Şablon | Görev |
|---|---|---|
| `DijitalKimlik.Domain` | classlib | Entity, value object, durum makinesi, iş kuralı. Bağımlılık yok |
| `DijitalKimlik.Application` | classlib | Use-case servisleri, repository arayüzleri, Result |
| `DijitalKimlik.Crypto` | classlib | JWS, SD-JWT, Status List (Bölüm 4) |
| `DijitalKimlik.Infrastructure` | classlib | EF Core, ADO.NET, Redis (Bölüm 2'den itibaren) |
| `DijitalKimlik.Issuer` | mvc | OpenID4VCI API ve memur portalı |
| `DijitalKimlik.Wallet` | mvc | Web cüzdanı |
| `DijitalKimlik.Verifier` | mvc | OpenID4VP API ve doğrulayıcı portalı |
| `DijitalKimlik.LegacySoap` | web | CoreWCF SOAP servisi |
| `DijitalKimlik.Worker` | worker | Windows servisi |
| `DijitalKimlik.UnitTests` | xunit | Domain ve Application testleri |
| `DijitalKimlik.IntegrationTests` | xunit | Uygulamayı bellek içinde ayağa kaldıran testler |

## 3. Adım adım

```powershell
. .\scripts\ortam.ps1
dotnet new sln -n DijitalKimlik                       # .NET 10: DijitalKimlik.slnx üretir
dotnet new classlib -o src/DijitalKimlik.Domain        # Application, Crypto, Infrastructure aynı şekilde
dotnet new mvc      -o src/DijitalKimlik.Issuer        # Wallet, Verifier aynı şekilde
dotnet new web      -o src/DijitalKimlik.LegacySoap
dotnet new worker   -o src/DijitalKimlik.Worker
dotnet new xunit    -o tests/DijitalKimlik.UnitTests   # IntegrationTests aynı şekilde

# Çözüme ekle (src/ ve tests/ klasörleriyle)
Get-ChildItem src,tests -Recurse -Filter *.csproj | ForEach-Object {
    dotnet sln add $_.FullName --solution-folder $_.Directory.Parent.Name }

# Bağımlılık yönünü proje referanslarıyla kur
dotnet add src/DijitalKimlik.Application reference src/DijitalKimlik.Domain src/DijitalKimlik.Crypto
dotnet add src/DijitalKimlik.Infrastructure reference src/DijitalKimlik.Application
dotnet add src/DijitalKimlik.Issuer reference src/DijitalKimlik.Application src/DijitalKimlik.Infrastructure

# Paket ekle: sürüm Directory.Packages.props'a yazılır
dotnet add tests/DijitalKimlik.UnitTests package Microsoft.Extensions.TimeProvider.Testing

dotnet build      # 0 uyarı, 0 hata
dotnet test       # 35/35
```

**`.slnx`:** .NET 10 SDK, `dotnet new sln` ile eski `.sln` yerine XML tabanlı `.slnx` üretir.
Eski biçim GUID'lerle dolu ve merge çakışmasına çok açıktı. Yenisi okunaklı. Güncel Visual
Studio ve VS Code (C# Dev Kit) ikisini de açar. Eski bir IDE kullanılacaksa
`dotnet sln migrate` ile dönüşüm yapılabilir.

**Portlar:** yerel geliştirmede yalnız HTTP: Issuer `5101`, Wallet `5201`, Verifier `5301`,
LegacySoap `5401`. HTTPS ve HSTS üretim ayarında açık (Bölüm 12).

## 4. Domain modeli

### 4.1 Yabancı kimlik numarası (value object)

`src/DijitalKimlik.Domain/Foreigners/ForeignerIdentityNumber.cs`

Yabancı kimlik numarası 11 hanelidir ve 99 ile başlar. Son iki hane, T.C. kimlik numarasındaki
algoritmayla hesaplanır:

- 10. hane = ((1., 3., 5., 7., 9. hanelerin toplamı) × 7 − (2., 4., 6., 8. hanelerin toplamı)) mod 10
- 11. hane = (ilk 10 hanenin toplamı) mod 10

Örnek, `991234567__`: tekler 9+1+3+5+7 = 25, çiftler 9+2+4+6 = 21, 10. hane (175−21) mod 10 = 4,
11. hane 50 mod 10 = 0. Sonuç: `99123456740`.

Bu sınıf bir **value object**:

- Kimliği yoktur, değeriyle karşılaştırılır (`record` eşitliği).
- Değişmezdir (immutable).
- **Geçersiz hâli oluşturulamaz.** Yapıcı private'tır; nesne yalnız `Create`/`TryCreate` ile
  ve doğrulamadan geçerek oluşur. Numarayı kullanan hiçbir kodun tekrar doğrulama yapması gerekmez.

Dikkat: C#'ta `%` negatif sonuç verebilir (`-3 % 10 == -3`). Kodda `((x % 10) + 10) % 10` ile
0–9 aralığına çekiliyor. Canlı kodlamada sık yapılan bir hata.

### 4.2 Yabancı ve ikamet izni (aggregate)

`Foreigner` bir **aggregate root**. İkamet izinleri yalnız `foreigner.GrantPermit(...)` ile
eklenir; `Permits` dışarıya `IReadOnlyCollection` olarak açılır. Böylece "aynı türde çakışan
iki aktif izin olamaz" kuralı tek yerde, kökte korunur. Kimse listeye doğrudan
`permits.Add(...)` yapamaz.

EF Core için her entity'de parametresiz `private` bir yapıcı var. EF nesneyi veritabanından
okurken onu kullanır. Uygulama kodu ise ancak kuralları uygulayan public yapıcıyı çağırabilir.

### 4.3 Kimlik bilgisinin durum makinesi

`src/DijitalKimlik.Domain/Credentials/IssuedCredential.cs`

```text
            Suspend                Revoke
  Active ──────────► Suspended ──────────► Revoked (son)
    ▲  │               │
    │  │  Reinstate    │ TryExpire
    └──┼───────────────┘      ▼
       └───────────────► Expired (son)
```

Geçişler bir tabloda tanımlı (`AllowedTransitions`). Geçersiz bir geçiş `DomainException`
fırlatır. İptal ve askı için gerekçe zorunlu. `StatusListValue`, Token Status List'e yazılacak
değeri verir: 0 geçerli, 1 geçersiz, 2 askıda.

Veri minimizasyonu (KVKK): kurum, verdiği SD-JWT'nin kendisini saklamaz. Yalnız iptal ve
denetim için gereken üst veriyi tutar.

### 4.4 Teklif: PIN denemesi ve özet saklama

`src/DijitalKimlik.Domain/Offers/CredentialOffer.cs`

- Pre-authorized code ve PIN **düz metin saklanmaz**, yalnız SHA-256 özetleri saklanır.
- 6 haneli bir PIN'in tek başına özeti kaba kuvvetle bulunur, çünkü yalnız 10⁶ olasılık var. Bu
  yüzden PIN, yüksek entropili kodla birlikte özetleniyor: `SHA-256(kod + ":" + PIN)`.
- Karşılaştırma `CryptographicOperations.FixedTimeEquals` ile yapılıyor. Normal `==`, ilk farklı
  baytta durur; yanıt süresinden doğru baytlar tahmin edilebilir (timing attack).
- Beş hatalı denemeden sonra teklif kilitleniyor. Kilitli teklife doğru PIN de girilse sonuç
  değişmiyor. Bunu bir test doğruluyor.

### 4.5 Id seçimi: int mi, GUID mi?

İç kimlikler `int IDENTITY`. Rastgele GUID clustered index'te her eklemede sayfanın ortasına
yazılır; sayfa bölünmesi (page split) ve parçalanma (fragmentation) oluşur. Dışarıya açılan
kimlikler ise ayrı ve tahmin edilemez değerler (`PublicId` GUID'i, `urn:uuid:` kimliği). Böylece
`/offers/1`, `/offers/2` gibi ardışık değerlerle kayıt taranamaz (IDOR riski).

## 5. Application katmanı

### 5.1 Result deseni

`src/DijitalKimlik.Application/Common/Result.cs`

Beklenen hatalar (kayıt zaten var, numara geçersiz) **dönüş değeriyle** bildirilir:
`Result<int>`. Exception yalnız beklenmeyen durumlar içindir (veritabanı kapalı). Gerekçe:

- Hata yolu metot imzasında görünür.
- Exception pahalıdır (stack trace toplanır).
- Akış kontrolü için exception kullanmak okunurluğu düşürür.

### 5.2 Use-case servisi ve bağımlılıklar

`ForeignerService` üç bağımlılığı yapıcıdan (primary constructor) alır: `IForeignerRepository`,
`IUnitOfWork`, `TimeProvider`.

- `TimeProvider` (.NET 8+), `DateTime.Now`'ın test edilebilir hâlidir. Testte
  `FakeTimeProvider` ile saat sabitleniyor.
- Servis, check-then-act yarışına dikkat ediyor. "Kayıt var mı?" kontrolü kullanıcıya düzgün mesaj
  vermek için. İki istek aynı anda kontrolü geçebilir. Asıl güvence veritabanındaki **unique
  index**'tir (Bölüm 2).

### 5.3 DI ömürleri

`DependencyInjection.AddApplication()`:

| Ömür | Anlamı | Burada |
|---|---|---|
| Singleton | Uygulama boyunca tek örnek | `TimeProvider.System` |
| Scoped | HTTP isteği başına bir örnek | `ForeignerService`, ileride `DbContext` |
| Transient | Her istendiğinde yeni örnek | Durumsuz, hafif yardımcılar |

**Captive dependency:** Singleton bir servis Scoped bir servisi yapıcıdan alırsa, Scoped servis
fiilen singleton olur. DbContext istekler arasında paylaşılır ve thread-safe olmadığı için bozulur.
ASP.NET Core, Development ortamında bunu `ValidateScopes` ile başlangıçta yakalar. Windows
servisinde (Bölüm 11) bu hatayı bilinçli olarak üretip çözeceğiz.

## 6. Testler

| Dosya | Ne test ediliyor |
|---|---|
| `Domain/ForeignerIdentityNumberTests.cs` | Geçerli ve geçersiz numaralar (`Theory` + `InlineData`), elle hesaplanan kontrol hanesi |
| `Domain/ForeignerTests.cs` | Yaş hesabı (doğum günü, artık yıl), çakışan izin kuralı |
| `Domain/IssuedCredentialTests.cs` | Durum makinesi geçişleri, son durumlar, zorunlu gerekçe, süre dolumu |
| `Domain/CredentialOfferTests.cs` | Tek kullanımlık kod, 5 hatalı denemede kilit, süre aşımı |
| `Application/ForeignerServiceTests.cs` | Kayıt, mükerrer kayıt, Domain hatasının Result'a dönüşmesi |
| `IntegrationTests/IssuerSmokeTests.cs` | Issuer uygulaması bellek içinde açılıyor, ana sayfa 200 dönüyor |

Testler **AAA** düzeninde (Arrange, Act, Assert). Mock kütüphanesi yerine elle yazılmış sahte
nesneler (fake) kullanıldı. Bağımlılık az olduğunda fake daha okunaklıdır ve kütüphane riski
taşımaz. Ortak nesne kurulumu `TestData` sınıfında (**Object Mother** deseni).

```text
Başarılı!  - Başarısız: 0, Başarılı: 34, Toplam: 34 - DijitalKimlik.UnitTests.dll
Başarılı!  - Başarısız: 0, Başarılı:  1, Toplam:  1 - DijitalKimlik.IntegrationTests.dll
```

## 7. CI ve dal koruması

`.github/workflows/ci.yml` her PR'da ve `main`'e her push'ta çalışır:

1. Kodu al.
2. `global.json`'daki SDK'yı kur.
3. `dotnet restore`.
4. `dotnet build -c Release`. Uyarılar hata sayılır.
5. `dotnet format --verify-no-changes`. Biçim bozuksa kırmızı.
6. `dotnet test` ve kod kapsamı (coverlet). Sonuçlar artifact olarak saklanır.

Kalite ve güvenlik kararları:

- `permissions: contents: read`: iş akışının token'ı yalnız okuyabilir (en az yetki).
- `concurrency`: aynı dala yeni push gelince eski çalışma iptal edilir.
- Action'lar **commit SHA'sına** sabitlendi (`actions/checkout@3d3c42e… # v7.0.1`). Bir etiket
  (`v7`) sonradan başka bir commit'e taşınabilir; ele geçirilmiş bir action deposu bütün
  kullanıcılarına kötü kod çalıştırabilir. SHA değişmez. Güncellemeyi Dependabot yapar.
- Runner `ubuntu-24.04`'e sabitlendi. `ubuntu-latest` habersiz değişebiliyor.

`main` dalının koruması (`gh api -X PUT repos/.../branches/main/protection`):

- PR zorunlu, doğrudan push yok. Yönetici de bu kurala tabi (`enforce_admins`).
- "Derleme ve test" kontrolü geçmeden merge yok. Dal `main` ile güncel olmalı (`strict`).
- Doğrusal geçmiş: yalnız squash merge. Force push ve dal silme kapalı.

`.github/dependabot.yml`: NuGet paketleri ve GitHub Actions için haftalık güncelleme PR'ları.

## 8. Çıkan sorunlar ve çözümleri

| # | Belirti | Neden | Çözüm |
|---|---|---|---|
| 1 | `error NU1008: ... PackageReference öğeleri Sürüm için bir değer tanımlayamaz` | Test ve Worker şablonları sürümü `.csproj`'ye yazıyor; merkezi paket yönetimi buna izin vermiyor | Sürümler `Directory.Packages.props`'a taşındı, `.csproj`'lerden silindi |
| 2 | SDK mesajları Türkçe | İşletim sistemi dili | Sorun değil; ararken hata kodunu (NU1008, CS8618) kullanmak yeterli |
| 3 | `dotnet add package` sürümü yanlış gruba ekledi | Komut, sürümü her zaman ilk `ItemGroup`'a yazıyor | `Directory.Packages.props`'ta tek, alfabetik bir `ItemGroup` |
| 4 | `dotnet format`: `ENDOFLINE` hataları | Şablon dosyaları Windows'ta CRLF ile üretildi, `.editorconfig` LF istiyor | `dotnet format` ile otomatik düzeltildi. `.editorconfig`'e `end_of_line = lf` yazıldı ki Windows ve Linux CI aynı sonucu versin |
| 5 | `dotnet format`: `WHITESPACE ... '\n' öğesini ekleyin` | `.editorconfig`'te `dotnet_separate_import_directive_groups = true`: `using` grupları arasında boş satır isteniyor | Otomatik düzeltildi; yeni kodda kurala uyuluyor |
| 6 | CI uyarısı: *Node.js 20 is deprecated* | `actions/*@v4` sürümleri Node 20 kullanıyor | v7/v6/v7 sürümlerine geçildi ve SHA'ya sabitlendi |
| 7 | CI notu: *ubuntu-latest will migrate to Ubuntu 26* | Etiket yeni imaja geçiyor | `runs-on: ubuntu-24.04` |
| 8 | Yaş testi yanlış kurulmuştu | Testi yazarken 2013 doğumlu birinin 2026'da 18 yaşında olduğu varsayılmış (aslında 13) | Test sade tarihlerle yeniden yazıldı. Ders: test kodu da kod; yanlış beklenti yanlış güven verir |
| 9 | (beklenen ama çıkmayan) `WebApplicationFactory<Program>` | Eskiden top-level `Program` sınıfı internal olduğu için `public partial class Program;` eklemek gerekirdi | .NET 10'da ek bir şey gerekmeden derlendi |

## 9. Mülakat soruları ve örnek cevaplar

**S: Çok katmanlı mimariyi anlatın. Katmanlar arasındaki bağımlılık nasıl olmalı?**
C: Sunum, uygulama (iş akışı), domain (iş kuralı) ve altyapı (veritabanı, dış servis) katmanları
var. Bağımlılıklar içe doğru: Domain hiçbir şeye bağlı değil. Altyapı, uygulama katmanının
tanımladığı arayüzleri uygular; DI ile çalışma anında bağlanır. Böylece iş kuralları
veritabanından bağımsız test edilir ve teknoloji değişikliği tek katmanda kalır. Bizim projede
`IForeignerRepository` arayüzü Application'da, EF Core uygulaması Infrastructure'da.

**S: SOLID'i projeden örneklerle anlatın.**
C:
- **S (Tek sorumluluk):** `ForeignerService` yalnız kayıt akışını yönetir. Kural `Foreigner`'da,
  kalıcılık repository'de.
- **O (Açık/kapalı):** Yeni bir kimlik bilgisi formatı (ör. mso_mdoc), mevcut kodu değiştirmeden
  yeni bir strategy sınıfıyla eklenecek (Bölüm 5).
- **L (Liskov):** `IForeignerRepository`'nin EF uygulaması da test fake'i de aynı sözleşmeye uyar.
  Servis hangisiyle çalıştığını bilmez ve davranışı değişmez.
- **I (Arayüz ayrımı):** Repository arayüzleri aggregate başına küçük; tek dev bir
  `IRepository<T>` yok.
- **D (Bağımlılığın tersine çevrilmesi):** Servis somut `DbContext`'e değil `IUnitOfWork`'e,
  `DateTime.Now`'a değil `TimeProvider`'a bağlı.

**S: Entity ile value object farkı nedir?**
C: Entity'nin kimliği vardır; özellikleri değişse de aynı nesnedir (bir yabancının adı
düzeltilse de aynı kişidir). Value object'in kimliği yoktur, değeriyle karşılaştırılır ve
değişmezdir (yabancı kimlik numarası, para tutarı, adres). Value object geçersiz hâlde
oluşturulamaz; doğrulama tek yerde yapılır.

**S: EF Core zaten repository ve unit of work değil mi? Neden repository yazdınız?**
C: Evet, `DbSet` bir repository, `DbContext` bir unit of work. Ayrı repository yazmanın iki
gerekçesi var: Application katmanının EF'e bağımlı olmaması ve aggregate kurallarının korunması
(yalnız kök üzerinden erişim). Bedeli fazladan katman. Küçük CRUD uygulamalarında `DbContext`'i
doğrudan kullanmak da makul bir tercihtir. Kararı projenin büyüklüğü belirler.

**S: Transient, Scoped ve Singleton farkı nedir? Captive dependency nedir?**
C: Transient her çözümlemede yeni örnek; Scoped istek (scope) başına bir örnek; Singleton
uygulama boyunca tek örnek. Captive dependency: uzun ömürlü bir servisin kısa ömürlü birini
tutması. En sık görülen hâli Singleton'ın içinde Scoped DbContext tutmak: DbContext istekler
arasında paylaşılır, thread-safe olmadığı için bozulur. Çözüm, ömürleri uyumlu seçmek ya da
gerektiğinde `IServiceScopeFactory` ile scope açmak.

**S: Beklenen hatalarda exception mı, dönüş değeri mi kullanırsınız?**
C: Beklenen iş hatalarında Result gibi bir dönüş değeri. Hata yolu imzada görünür ve exception'ın
maliyeti yok. Beklenmeyen, programın devam edemeyeceği durumlarda exception. Domain katmanında
invariant ihlali exception'dır (geçersiz nesne asla oluşmamalı); servis bunu yakalayıp Result'a
çevirir.

**S: Unit test ile integration test farkı nedir? Fact ve Theory?**
C: Unit test bir birimi dış bağımlılıkları olmadan, milisaniyeler içinde test eder (34 test
337 ms'de bitti). Integration test birden fazla bileşeni gerçek ya da gerçeğe yakın ortamda test
eder (uygulamayı bellek içinde ayağa kaldıran `WebApplicationFactory`). xUnit'te `[Fact]`
parametresiz tek bir durumdur. `[Theory]` + `[InlineData]` aynı testi farklı girdilerle çalıştırır.

**S: Mock, stub ve fake farkı nedir?**
C: Stub sabit cevap döner. Mock, beklenen çağrıların yapılıp yapılmadığını doğrular
(etkileşim testi). Fake basit ama çalışan bir uygulamadır (bellek içi liste ile repository).
Biz fake kullandık: durumu doğrulamak (kayıt eklendi mi, kaç kez kaydedildi) davranış
doğrulamaktan daha az kırılgan.

**S: Neden rastgele GUID yerine int Id?**
C: Rastgele GUID clustered index'te sıralı değildir: her ekleme sayfanın ortasına gider, sayfa
bölünmesi ve parçalanma artar, indeks büyür (16 bayta karşılık 4 bayt). Sıralı Id dışarı açılırsa
tahmin edilebilir; bu yüzden URL ve token'larda ayrı, tahmin edilemez bir kimlik kullanıyoruz.

**S: Bir kodu veya parolayı veritabanında nasıl saklarsınız?**
C: Hiçbir zaman düz metin olarak saklamam. Kısa ömürlü yüksek entropili kodlar için SHA-256
özeti yeterli. Kullanıcı parolaları için yavaş ve tuzlu (salt) algoritmalar gerekir: PBKDF2,
bcrypt, Argon2. ASP.NET Core Identity PBKDF2 kullanır. Karşılaştırma sabit zamanlı yapılmalı.
Düşük entropili değerlerin (6 haneli PIN) tek başına özeti kaba kuvvetle çözülür.

**S: .NET Framework, .NET Core ve .NET 5+ farkı nedir?**
C: .NET Framework yalnız Windows'ta çalışır ve son sürümü 4.8.x; bakım modunda. .NET Core
açık kaynaklı ve çapraz platformdur, Linux konteynerinde çalışır. .NET 5'ten itibaren adındaki
"Core" düştü ve tek platform oldu: .NET 5, 6, 7, 8, 9, 10. Yeni projeler .NET 10 (LTS) ile
yazılır. WCF sunucusu, Web Forms ve `System.Web` .NET Framework'te kaldı.

**S: CI nedir? Sizin pipeline'ınızda hangi adımlar var?**
C: Sürekli entegrasyon: her değişiklik paylaşılmadan önce otomatik olarak derlenir ve test
edilir; hata, değişikliğin sahibine dakikalar içinde döner. Bizde her PR'da Release derlemesi
(uyarılar hata), biçim denetimi, testler ve kod kapsamı çalışıyor. `main` korumalı: kontrol
yeşil olmadan merge yok.

**Canlı kodlama ihtimali:** "T.C. kimlik numarası doğrulayan bir fonksiyon yazın." Bölüm 4.1'deki
algoritmayı ezbere bilin. Tuzaklar: 11 hane kontrolü, rakam olmayan karakter, ilk hanenin 0
olamaması (T.C. için), negatif mod.
