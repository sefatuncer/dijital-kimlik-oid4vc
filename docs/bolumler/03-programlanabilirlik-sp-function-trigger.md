# Bölüm 3: Stored Procedure, Function, Trigger ve Temporal Table

| | |
|---|---|
| İlan maddeleri | **k** (Stored Procedure, Function, Trigger), **j** (MSSQL), **f/i** (ADO.NET), **l** (değişiklik yönetimi: veritabanı dağıtımı), **c** (güvenli yazılım) |
| Sonuç | 4 programlanabilir nesne, temporal table, ADO.NET ile SP çağrısı, en az yetki testi, CI'da veritabanı dağıtım provası; 68 test |
| PR | https://github.com/sefatuncer/dijital-kimlik-oid4vc/pull/4 |

## 1. Amaç

İş kuralının bir kısmı veritabanında da yaşayabilir. Bu bölümde her nesne türü, projede gerçek
bir işe karşılık gelecek şekilde eklendi. Her birinin C# tarafındaki alternatifiyle karşılaştırması
da yapıldı.

| Nesne | Adı | Gerçek işi |
|---|---|---|
| Scalar function | `issuer.ufn_IdentityNumberCheckDigits` | Yabancı kimlik numarasının kontrol haneleri (döngüyle) |
| Inline TVF | `issuer.itvf_IdentityNumberCheckDigits` | Aynı hesap tek `SELECT` ile |
| Stored procedure | `issuer.usp_RedeemOffer` | Teklif kullanımı: dördüncü eşzamanlılık yöntemi |
| Trigger | `issuer.trg_IssuedCredentials_StatusAudit` | Kimlik bilgisi durumu değişince denetim kaydı |
| Temporal table | `issuer.ResidencePermits` + `ResidencePermitsHistory` | İkamet izninin her eski hâli |

## 2. Function: scalar mı inline mı?

İki function aynı sonucu verir. Farkları:

- **Scalar function** her satır için ayrı çağrılır. Okunması kolaydır ama büyük sorguda yavaştır.
  SQL Server 2019'dan itibaren bazı scalar function'lar satır içine açılabilir (inlining). Bu
  function'daki `WHILE` döngüsü buna engeldir.
- **Inline TVF** tek bir `SELECT`'tir. Optimizer onu çağıran sorgunun içine açar, view gibi.

Test (`ProgrammabilityTests.cs`) iki function'ı C# algoritmasıyla karşılaştırıyor:

- 300 rastgele girdi tek sorguda gidiyor.
- Liste JSON parametresi olarak yollanıyor; `OPENJSON` onu satırlara açıyor.
- Inline TVF her satıra `CROSS APPLY` ile uygulanıyor.

## 3. Stored procedure ve ADO.NET

`usp_RedeemOffer` kararı tek bir `UPDATE` ile verir. `OUTPUT ... INTO @Changed` güncellenen
satırın yeni durumunu aynı komutta döndürür. Böylece yanlış PIN'de "WrongTxCode" ile "Locked"
kesin ayrılır. EF'in `ExecuteUpdate`'i bunu veremiyordu (Bölüm 2, son sorun).

`StoredProcedureOfferRedeemer.cs`, SP'yi ADO.NET ile çağırır. Çağrı şöyle kurulur:

- `CommandType.StoredProcedure`.
- Parametrelerin tipi ve boyutu açıkça verilir. `AddWithValue` her farklı uzunluk için ayrı plan
  ve örtük dönüşüm üretir.
- Bağlantı EF'ten alınır: `OpenConnectionAsync` ve `CloseConnectionAsync`. Varsa mevcut
  transaction'a katılır.

```csharp
command.Parameters.Add(new SqlParameter("@PreAuthorizedCodeHash", SqlDbType.Char, 43) { Value = hash });
```

## 4. Trigger: denetim kaydı

Trigger üç kurala göre yazıldı:

- **Çok satır güvenli.** `inserted` ve `deleted` tabloları birden çok satır içerebilir. Tek satır
  varsayan trigger klasik bir hatadır. Test tek bir `UPDATE` ile 3 satır değiştirir ve 3 denetim
  kaydı bekler.
- **`UPDATE(Status)` yetmez.** Bu fonksiyon yalnız sütunun `SET` listesinde olup olmadığını söyler.
  Değer gerçekten değişti mi diye eski ve yeni değer ayrıca karşılaştırılır.
- **Kimin yaptığı kaydedilir.** Uygulama kullanıcı adını `sp_set_session_context` ile bağlantıya
  yazar, trigger bunu `SESSION_CONTEXT` ile okur. Bağlantı havuza dönünce `sp_reset_connection`
  bu değeri siler.

**Ownership chaining:** Uygulama kullanıcısının `audit` şemasına `INSERT` yetkisi yok. Trigger ve
tablo aynı sahibe (dbo) ait olduğu için zincir kopmaz, yetki kontrolü yapılmaz. Test, gerçek
`db/00` betiğini uygulayarak üç şeyi kanıtlıyor:

- Uygulama kullanıcısının doğrudan `INSERT` denemesi 229 hatası alıyor.
- `DROP TABLE` denemesi 3701 hatası alıyor.
- Durum güncellemesi ise denetim kaydını yazıyor.

**EF ile trigger:** EF Core SQL Server'da `SaveChanges` sırasında `OUTPUT` kullanır. Tetikleyicisi
olan tabloda bu hata verir: "cannot have any enabled triggers if the statement contains an
OUTPUT clause without INTO clause". Çözüm, `IssuedCredentialConfiguration` içinde
`t.HasTrigger(...)` tanımıdır. Bunu görünce EF yeni değerleri ayrı bir `SELECT` ile okur.

## 5. Temporal table

`ResidencePermitConfiguration`'da `IsTemporal` tanımı var. SQL Server her güncellemede eski satırı
`ResidencePermitsHistory`'ye kendisi taşır. Uygulama geçmiş kaydı yazmaz. Okuma tarafında
`TemporalAll()` iznin bütün hâllerini, `TemporalAsOf(t)` ise t anındaki hâlini verir. Dönem
sütunları (`PeriodStart`, `PeriodEnd`) `HIDDEN` olarak tanımlı, `SELECT *` sorgusunda görünmez.

## 6. Dağıtım: idempotent betik ve migration bundle

| Yol | Kim kullanır | Nasıl |
|---|---|---|
| İdempotent SQL betiği | DBA, değişiklik onayıyla | `sqlcmd -I -b -i issuer-idempotent.sql` |
| Migration bundle | Otomatik kurulum boru hattı | `./issuer-efbundle --connection "..."` |

Programmability migration'ı SQL metnini dosyadan okumaz, metin migration'ın içinde sabittir.
Dosyadan okunsaydı, dosya değiştiğinde eski migration da sessizce değişirdi.

**Sorun:** EF idempotent betikte her migration'ı `IF NOT EXISTS (...) BEGIN ... END` içine koyar.
`CREATE FUNCTION/PROCEDURE/TRIGGER` ise batch'in ilk ifadesi olmak zorundadır, bu yüzden betik
geçersizdi. **Çözüm:** Migration'daki `Exec()` yardımcısı her `CREATE`'i `EXEC(N'...')` ile sarar.
Tek tırnaklar ikilenir; SQL yorumlarındaki kesme işaretleri de buna dahil.

**İkinci sorun:** sqlcmd varsayılan olarak `QUOTED_IDENTIFIER OFF` ile bağlanır. Filtreli indeks
ise ON ister (Msg 1934). Bu yüzden komut satırında `-I` şarttır.

**CI'da prova** (`ci.yml`, "Veritabanı dağıtım provası" işi):

1. SQL Server service container'ı başlatılır.
2. İdempotent betik boş bir veritabanına **iki kez** uygulanır. İkinci çalıştırma hata vermemeli.
3. Bundle başka bir veritabanına iki kez çalıştırılır. İkincisi "No migrations were applied"
   der.
4. İki veritabanında migration, programlanabilir nesne ve temporal tablo sayıları karşılaştırılır.
   İlk CI çalıştırmasında ikisi de `3,4,1` verdi.

## 7. Çıkan sorunlar ve dersler

| Sorun | Ders |
|---|---|
| Trigger eklenince `SaveChanges` hata verdi | EF'e trigger'ı `HasTrigger` ile bildir |
| İdempotent betik `CREATE` yüzünden geçersiz | Programlanabilir nesneyi `EXEC(N'...')` ile sar, betiği CI'da iki kez dene |
| sqlcmd'de Msg 1934 | `-I` bayrağı; SSMS'in varsayılanı ON, sqlcmd'ninki OFF |
| `SESSION_CONTEXT` değeri kayboluyor | Bağlantı havuza dönünce sıfırlanır; değer aynı açık bağlantıda yazılmalı |
| `RedeemResult` sayıları SP'de sabit | Enum değerleri açıkça yazıldı ve SP'ye bağımlı olduğu belgelendi |

## 8. Mülakat notları

- "SP mi, ORM mi?" sorusuna cevap: dört yöntemin karşılaştırması.
  - SP'nin artısı: tek tur ve `OUTPUT`.
  - SP'nin eksisi: kural iki yerde yaşar, sürümlemesi migration ile yapılır, birim testi zordur.
- "Trigger'ın riskleri?" sorusuna cevap:
  - gizli iş kuralı
  - çok satır hatası
  - performans
  - EF ile çakışma (`HasTrigger`)
- "Scalar function neden yavaş?" sorusuna cevap: satır satır çağrı. Alternatifi inline TVF.
- "Bir tablonun geçmişini nasıl tutarsın?" sorusuna cevap: temporal table, trigger ile audit,
  ya da uygulamada event.
- "Veritabanı değişikliğini canlıya nasıl alırsın?" sorusuna cevap: idempotent betik, DBA onayı,
  CI'da iki kez prova, `-I -b`.
