# Geliştirme Notu 3: Stored Procedure, Function, Trigger ve Temporal Table

| | |
|---|---|
| Kapsam | Veritabanında çalışan kod (SP, function, trigger), tarihçe tutma, ADO.NET, veritabanı dağıtımı |
| Sonuç | 4 programlanabilir nesne, temporal table, ADO.NET ile SP çağrısı, en az yetki testi, CI'da veritabanı dağıtım provası; 68 test |
| PR | https://github.com/sefatuncer/dijital-kimlik-oid4vc/pull/4 |

## 1. Amaç

İş kuralının bir kısmı veritabanında da çalışabilir. SQL Server'da bunun araçları şunlardır:

- **Stored procedure (SP, saklı yordam):** veritabanında saklanan, adıyla çağrılan SQL komut grubu.
  Parametre alır, sonuç döndürür.
- **Function (fonksiyon):** bir değer ya da tablo döndüren, sorgunun içinde kullanılabilen SQL kodu.
  Veriyi değiştiremez.
- **Trigger (tetikleyici):** bir tabloya `INSERT`, `UPDATE` ya da `DELETE` yapılınca kendiliğinden
  çalışan SQL kodu.
- **Temporal table (zamansal tablo):** her satırın bütün eski hâllerini SQL Server'ın kendisinin
  sakladığı tablo.

Bu notta her nesne türü, projede gerçek bir işe karşılık gelecek şekilde eklendi. Her birinin C#
tarafındaki alternatifiyle karşılaştırması da yapıldı.

| Nesne | Adı | Gerçek işi |
|---|---|---|
| Scalar function | `issuer.ufn_IdentityNumberCheckDigits` | Yabancı kimlik numarasının kontrol haneleri, döngüyle |
| Inline TVF | `issuer.itvf_IdentityNumberCheckDigits` | Aynı hesap, tek `SELECT` ile |
| Stored procedure | `issuer.usp_RedeemOffer` | Teklif kullanımı: dördüncü eşzamanlılık yöntemi |
| Trigger | `issuer.trg_IssuedCredentials_StatusAudit` | Kimlik bilgisinin durumu değişince denetim kaydı |
| Temporal table | `issuer.ResidencePermits` + `ResidencePermitsHistory` | İkamet izninin her eski hâli |

Adlardaki önekler bir adlandırma kuralıdır: `ufn` kullanıcı tanımlı scalar fonksiyon, `itvf` inline
tablo fonksiyonu, `usp` kullanıcı SP'si, `trg` trigger. Önek, nesnenin türünü adından belli eder.

## 2. Function: scalar mı inline mı?

İki function aynı sonucu verir: kimlik numarasının ilk 9 hanesinden son 2 haneyi hesaplar.
Farkları nasıl çalıştıklarındadır:

- **Scalar function** tek bir değer döndürür. Sorgu 10.000 satır okuyorsa fonksiyon 10.000 kez ayrı
  ayrı çağrılır. Okunması kolaydır ama büyük sorguda yavaştır. SQL Server 2019'dan itibaren bazı
  scalar function'lar sorgunun içine açılabilir. Buna **inlining** denir. Ama bu fonksiyondaki
  `WHILE` döngüsü inlining'e engeldir.
- **Inline TVF** (table-valued function, tablo döndüren fonksiyon) tek bir `SELECT`'ten oluşur.
  **Optimizer** (sorgunun en hızlı nasıl çalışacağına karar veren SQL Server bileşeni) onu çağıran
  sorgunun içine açar, tıpkı bir view gibi. Sonuç: 10.000 ayrı çağrı yerine tek bir sorgu planı.

Örnek kullanım:

```sql
-- Scalar: her satır için ayrı çağrı
SELECT IdentityNumber, issuer.ufn_IdentityNumberCheckDigits(LEFT(IdentityNumber, 9)) AS Kontrol
FROM issuer.Foreigners;

-- Inline TVF: CROSS APPLY her satıra uygular, ama optimizer bunu tek plana açar
SELECT f.IdentityNumber, c.CheckDigits AS Kontrol
FROM issuer.Foreigners AS f
CROSS APPLY issuer.itvf_IdentityNumberCheckDigits(LEFT(f.IdentityNumber, 9)) AS c;
```

Sonuç: iki sorgu da aynı kontrol hanelerini verir. Satır sayısı arttıkça ikinci sorgu belirgin
şekilde hızlıdır.

`CROSS APPLY`, sol tablodaki her satır için sağdaki fonksiyonu çalıştırıp sonucu yanına ekler.

Test (`ProgrammabilityTests.cs`) iki fonksiyonu C# algoritmasıyla karşılaştırıyor:

1. 300 rastgele girdi üretilir.
2. Liste tek bir JSON parametresi olarak gönderilir. `OPENJSON`, JSON metnini satırlara açan SQL
   Server fonksiyonudur. Böylece 300 girdi için 300 ayrı sorgu değil, tek sorgu gider.
3. Inline TVF her satıra `CROSS APPLY` ile uygulanır.
4. Her satırda SQL sonucu ile C# sonucu aynı olmalıdır.

## 3. Stored procedure ve ADO.NET

`usp_RedeemOffer` kararı tek bir `UPDATE` ile verir:

- PIN doğruysa durumu `Redeemed` yapar.
- PIN yanlışsa deneme sayısını artırır. Sayı 5'e ulaştıysa durumu `Locked` yapar.

`OUTPUT ... INTO @Changed` ifadesi, güncellenen satırın **yeni durumunu** aynı komutta bir tablo
değişkenine yazar. Böylece yanlış PIN'de "bu deneme kilitledi mi?" sorusunun cevabı kesin bilinir:
`WrongTxCode` (yanlış, ama hâlâ deneme hakkı var) ile `Locked` (kilitlendi) karışmaz. EF'in
`ExecuteUpdate`'i yeni durumu döndüremiyordu (Not 2, son sorun).

`StoredProcedureOfferRedeemer.cs`, SP'yi **ADO.NET** ile çağırır. ADO.NET, .NET'in veritabanıyla
doğrudan konuşan alt düzey kütüphanesidir: `SqlConnection`, `SqlCommand`, `SqlParameter`. EF Core da
içeride ADO.NET kullanır. Çağrı şöyle kurulur:

- `CommandType.StoredProcedure`: komut metni SQL değil, SP'nin adıdır.
- Parametrelerin tipi ve boyutu açıkça verilir. `AddWithValue` kullanılmaz. `AddWithValue` tipi
  değerden tahmin eder: C# `string`'i `nvarchar(uzunluk)` olarak gönderir. Bu iki soruna yol açar:
  - Sütun `char` ya da `varchar` ise SQL Server sütunu dönüştürmek zorunda kalır. Buna örtük
    dönüşüm (`CONVERT_IMPLICIT`) denir. İndeks kullanılamaz ve tablo taranır.
  - Her farklı uzunluk için ayrı bir sorgu planı oluşur ve plan önbelleği şişer.
- Bağlantı EF'ten alınır: `OpenConnectionAsync` ve `CloseConnectionAsync`. O sırada açık bir
  transaction varsa komut ona katılır.

```csharp
// Parametre sütunla aynı tipte: char(43). SQL Server dönüşüm yapmaz, indeksi kullanır (Index Seek)
command.Parameters.Add(new SqlParameter("@PreAuthorizedCodeHash", SqlDbType.Char, 43) { Value = hash });
```

Sonuç: SP, teklifi indeksle tek satırda bulur ve tek komutta günceller.

## 4. Trigger: denetim kaydı

Bir kimlik bilgisinin durumu değiştiğinde (askıya alındı, iptal edildi) bunun kaydı tutulmalıdır:
kim, ne zaman, eski ve yeni durum, gerekçe. `trg_IssuedCredentials_StatusAudit` trigger'ı bu kaydı
`audit.AuditLog` tablosuna yazar.

> [!NOTE]
> **Örnek.** Memur bir yabancının kimlik bilgisini "adres doğrulaması bekleniyor" gerekçesiyle askıya
> alır. Uygulama yalnız `IssuedCredentials` tablosunda `Status` sütununu günceller. Trigger aynı
> transaction içinde `audit.AuditLog`'a şu satırı ekler: tablo adı, kayıt anahtarı, eski durum
> `{"status":1}`, yeni durum `{"status":2,"reason":"adres doğrulaması bekleniyor"}` ve memurun
> kullanıcı adı. Uygulama kodu denetim kaydını unutsa bile kayıt yazılır.

Trigger üç kurala göre yazıldı:

- **Çok satırda doğru çalışır.** Trigger'ın içinde iki sanal tablo vardır: `inserted` (satırların
  yeni hâli) ve `deleted` (eski hâli). Tek bir `UPDATE` birden çok satırı değiştirirse bu tablolarda
  birden çok satır olur. Trigger'ı "tek satır gelir" diye yazmak klasik bir hatadır: 3 satır
  değişir, 1 kayıt yazılır. Test tek bir `UPDATE` ile 3 satır değiştirir ve 3 denetim kaydı bekler.
- **`UPDATE(Status)` yetmez.** Bu fonksiyon yalnız `Status` sütununun `SET` listesinde olup
  olmadığını söyler. Değerin gerçekten değişip değişmediğini söylemez: `SET Status = Status` da
  doğru döner. Bu yüzden eski ve yeni değer ayrıca karşılaştırılır: `WHERE i.Status <> d.Status`.
- **Kimin yaptığı kaydedilir.** Veritabanına bağlanan hep aynı uygulama kullanıcısıdır
  (`dk_issuer_app`), memur değil. Memurun adını trigger'a iletmek için uygulama, adı
  `sp_set_session_context` ile bağlantıya yazar. Trigger `SESSION_CONTEXT(N'UserName')` ile okur.
  Değer yoksa bağlantının login adını (`ORIGINAL_LOGIN()`) yazar.
  **Session context**, bir bağlantıya iliştirilen anahtar-değer bilgisidir. Bağlantı havuza
  döndüğünde `sp_reset_connection` bu değeri siler. Bu yüzden değer, güncellemeyle aynı açık
  bağlantıda yazılmalıdır.

**Ownership chaining (sahiplik zinciri):** Uygulama kullanıcısının `audit` şemasına `INSERT` yetkisi
yok. Yine de trigger denetim kaydını yazabiliyor. Neden: trigger ve `audit.AuditLog` tablosu aynı
sahibe (`dbo`) aittir. Bir nesne aynı sahibin başka bir nesnesini kullandığında SQL Server yetkiyi
yeniden denetlemez; zincir kopmaz. Sonuç: uygulama denetim kaydını **yalnız trigger yoluyla**
yazabilir, doğrudan yazamaz, değiştiremez, silemez.

Test, gerçek `db/00` betiğini uygulayarak üç şeyi kanıtlıyor:

- Uygulama kullanıcısının `audit` tablosuna doğrudan `INSERT` denemesi 229 hatası alır.
- `DROP TABLE` denemesi 3701 hatası alır.
- Durum güncellemesi ise denetim kaydını yazar.

**EF ile trigger:** EF Core, SQL Server'da `SaveChanges` sırasında yeni değerleri geri okumak için
`OUTPUT` ifadesini kullanır. Trigger'ı olan tabloda bu hata verir: *"cannot have any enabled
triggers if the statement contains an OUTPUT clause without INTO clause"*. Çözüm,
`IssuedCredentialConfiguration` içindeki `t.HasTrigger(...)` tanımıdır. EF, tabloda trigger
olduğunu bilince yeni değerleri `OUTPUT` yerine ayrı bir `SELECT` ile okur.

## 5. Temporal table

İkamet izni güncellendiğinde eski hâli kaybolmamalıdır. "Bu izin 1 Mart'ta hangi durumda ve hangi
bitiş tarihiyle kayıtlıydı?" sorusu cevaplanabilmelidir.

`ResidencePermitConfiguration`'da `IsTemporal` tanımı var. Bu tanımla SQL Server iki tablo tutar:
asıl tablo (`ResidencePermits`) ve tarihçe tablosu (`ResidencePermitsHistory`). Her güncellemede
eski satırı tarihçe tablosuna kendisi taşır. Uygulama kodu tarihçeye hiçbir şey yazmaz.

Okuma tarafında EF iki sorgu yöntemi sunar:

```csharp
// İznin bütün hâlleri, eskiden yeniye
var hepsi = await db.ResidencePermits.TemporalAll()
    .Where(p => p.PermitNumber == "IK-2026-000001")
    .OrderBy(p => EF.Property<DateTime>(p, "PeriodStart"))
    .ToListAsync(ct);

// İznin 1 Mart 2026'daki hâli
var o_gun = await db.ResidencePermits.TemporalAsOf(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc))
    .SingleAsync(p => p.PermitNumber == "IK-2026-000001", ct);
```

Sonuç: ilk sorgu her güncelleme için bir satır döndürür. İkinci sorgu tek satır döndürür: iznin
o tarihteki hâli.

Dönem sütunları (`PeriodStart`, `PeriodEnd`) satırın hangi zaman aralığında geçerli olduğunu tutar.
Bu sütunlar `HIDDEN` tanımlıdır: `SELECT *` sorgusunda görünmezler, eski sorgular etkilenmez.

## 6. Dağıtım: idempotent betik ve migration bundle

Veritabanı değişikliği canlıya iki yoldan alınabilir:

| Yol | Kim kullanır | Nasıl |
|---|---|---|
| İdempotent SQL betiği | DBA, değişiklik onayıyla | `sqlcmd -I -b -i issuer-idempotent.sql` |
| Migration bundle | Otomatik kurulum boru hattı | `./issuer-efbundle --connection "..."` |

`sqlcmd` bayrakları: `-i` çalıştırılacak dosya, `-I` QUOTED_IDENTIFIER açık (aşağıda neden), `-b`
hata olursa dur ve sıfırdan farklı çıkış koduyla bit. `-b` olmadan sqlcmd hatayı ekrana yazar ama
devam eder ve boru hattı hatayı fark etmez.

Programmability migration'ı SQL metnini dosyadan okumaz, metin migration'ın içinde sabittir. Neden:
dosyadan okunsaydı, dosya sonradan değiştiğinde eski migration da sessizce değişirdi. Kurulu bir
veritabanı ile yeni kurulan bir veritabanı farklı olurdu.

**Sorun 1:** EF, idempotent betikte her migration'ı `IF NOT EXISTS (...) BEGIN ... END` bloğuna koyar.
Oysa `CREATE FUNCTION`, `CREATE PROCEDURE` ve `CREATE TRIGGER` bir **batch**'in ilk ifadesi olmak
zorundadır. Batch, SQL Server'a tek seferde gönderilen komut grubudur; betikte `GO` satırlarıyla
ayrılır. `IF` bloğunun içinde `CREATE PROCEDURE` olamaz. Betik bu yüzden geçersizdi.

**Çözüm:** Migration'daki `Exec()` yardımcısı her `CREATE` ifadesini `EXEC(N'...')` ile sarar.
`EXEC` içindeki metin ayrı bir batch olarak çalışır, böylece `IF` bloğunun içinde durabilir.

```csharp
// Metindeki her tek tırnak ikilenir (' → ''), çünkü metin artık bir SQL string'inin içinde
private static string Exec(string sql) => $"EXEC(N'{sql.Replace("'", "''", StringComparison.Ordinal)}');";
```

Sonuç: betik `IF NOT EXISTS (...) BEGIN EXEC(N'CREATE OR ALTER PROCEDURE ...'); END` biçiminde
çıkar ve geçerlidir. Tırnak ikileme SQL yorumlarındaki kesme işaretlerini de kapsar.

**Sorun 2:** sqlcmd varsayılan olarak `QUOTED_IDENTIFIER OFF` ile bağlanır. Filtreli indeks ise bu
ayarın açık olmasını ister ve yoksa Msg 1934 hatası verir. SSMS'te ayar varsayılan olarak açık
olduğu için sorun orada görünmez. Bu yüzden komut satırında `-I` şarttır.

**CI'da prova** (`ci.yml`, "Veritabanı dağıtım provası" işi):

1. SQL Server bir **service container** olarak başlatılır. Service container, CI işi boyunca
   yanında çalışan yardımcı konteynerdir.
2. İdempotent betik boş bir veritabanına **iki kez** uygulanır. İkinci çalıştırma hata vermemeli ve
   hiçbir şeyi değiştirmemelidir.
3. Bundle başka bir veritabanında iki kez çalıştırılır. İkincisi "No migrations were applied" der.
4. İki veritabanında üç sayı karşılaştırılır: uygulanmış migration, programlanabilir nesne ve
   temporal tablo sayıları. İlk CI çalıştırmasında ikisi de `3,4,1` verdi: 3 migration, 4 nesne
   (2 function, 1 SP, 1 trigger), 1 temporal tablo.

Sonuç: iki dağıtım yolunun da aynı şemayı ürettiği ve tekrar çalıştırmaya dayanıklı olduğu her
PR'da kanıtlanır.

## 7. Çıkan sorunlar ve dersler

| Sorun | Ders |
|---|---|
| Trigger eklenince `SaveChanges` hata verdi | EF'e trigger'ı `HasTrigger` ile bildir |
| İdempotent betik `CREATE` yüzünden geçersizdi | Programlanabilir nesneyi `EXEC(N'...')` ile sar, betiği CI'da iki kez dene |
| sqlcmd'de Msg 1934 | `-I` bayrağı kullan. SSMS'in varsayılanı ON, sqlcmd'ninki OFF |
| `SESSION_CONTEXT` değeri kayboluyordu | Bağlantı havuza dönünce sıfırlanır. Değer, güncellemeyle aynı açık bağlantıda yazılmalı |
| `RedeemResult` enum sayıları SP'nin içinde sabit yazılı | Enum değerleri C#'ta açıkça yazıldı (`Success = 0` gibi) ve SP'ye bağımlı oldukları belgelendi. Enum sırası değişirse SP yanlış sonuç döndürürdü |

## 8. Terimler

| Terim | Ne demek? | Nerede geçer? |
|---|---|---|
| ADO.NET | .NET'in alt düzey veritabanı kütüphanesi | 3 |
| `AddWithValue` | Parametre tipini değerden tahmin eden metot; kullanılmaz | 3 |
| Batch / `GO` | Tek seferde gönderilen komut grubu / ayıracı | 6 |
| `CONVERT_IMPLICIT` | SQL Server'ın tip uyuşmazlığında yaptığı örtük dönüşüm | 3 |
| `CROSS APPLY` | Sol tablonun her satırına sağdaki fonksiyonu uygulamak | 2 |
| `inserted` / `deleted` | Trigger içindeki yeni ve eski satır tabloları | 4 |
| Inline TVF | Tek `SELECT`'ten oluşan, sorguya açılan tablo fonksiyonu | 2 |
| `OPENJSON` | JSON metnini satırlara açan SQL fonksiyonu | 2 |
| Optimizer | Sorgu planına karar veren SQL Server bileşeni | 2 |
| `OUTPUT ... INTO` | Değişen satırları aynı komutta bir tabloya yazmak | 3 |
| Ownership chaining | Aynı sahipli nesneler arasında yetki denetiminin atlanması | 4 |
| `QUOTED_IDENTIFIER` | Filtreli indeksin istediği oturum ayarı; sqlcmd'de `-I` | 6 |
| Scalar function | Tek değer döndüren, satır satır çağrılan fonksiyon | 2 |
| Service container | CI işinin yanında çalışan yardımcı konteyner | 6 |
| Session context | Bağlantıya iliştirilen anahtar-değer bilgisi | 4 |
| Stored procedure | Veritabanında saklanan, adıyla çağrılan SQL kodu | 1, 3 |
| Temporal table | Satırların eski hâllerini kendisi saklayan tablo | 5 |
| Trigger | Veri değişince kendiliğinden çalışan SQL kodu | 1, 4 |
