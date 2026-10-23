# Geliştirme Notu 4: Kripto Çekirdeği: JWS, SD-JWT VC ve Token Status List

| | |
|---|---|
| Proje | `src/DijitalKimlik.Crypto` |
| Testler | `tests/DijitalKimlik.UnitTests/Crypto/` (39 test) |
| Dış paket | Yok. Yalnız .NET'in `System.Security.Cryptography`, `System.Text.Json` ve `System.IO.Compression` API'leri |

## 1. Amaç

Dijital ikamet izni bir **SD-JWT VC** olarak verilir. SD-JWT VC, düzenleyicinin imzaladığı ve
sahibinin alanları tek tek gösterebildiği bir kimlik bilgisidir. Bir otel yalnız "adı ve iznin
geçerli olduğu" bilgisini ister; cüzdan doğum tarihini ve uyruğu göstermez, imza yine doğrulanır.

Bu fazda üç parça yazıldı:

| Parça | Ne işe yarar? | Kaynak |
|---|---|---|
| JWS (ES256) | Bir JSON'u imzalamak ve imzayı doğrulamak | RFC 7515, RFC 7518 |
| SD-JWT ve KB-JWT | Alanları özetle gizlemek, seçerek göstermek, sunumu cüzdan anahtarına bağlamak | RFC 9901 |
| Token Status List | Kimlik bilgisinin iptal ya da askıda olduğunu, kimin sorgulandığını söylemeden yayımlamak | draft-ietf-oauth-status-list-21 |

## 2. SD-JWT nasıl çalışır?

Düzenleyici her gizli alan için bir **disclosure** üretir: `[salt, ad, değer]` dizisinin base64url
hâli. Salt 16 baytlık rastgele bir değerdir. Salt olmasaydı "uyruk = SYR" özetini herkes hesaplayıp
eşleştirebilirdi.

İmzalı JWT'ye alanın kendisi değil, disclosure'ın SHA-256 özeti girer:

```text
İmzalı içerik:  { "iss": ..., "vct": ..., "cnf": {...}, "_sd": ["Cr8...", "Jz1...", ...], "_sd_alg": "sha-256" }
Cüzdana giden:  <imzalı JWT>~<given_name disclosure>~<family_name disclosure>~...~
Doğrulayıcıya:  <imzalı JWT>~<given_name disclosure>~<permit_type disclosure>~<KB-JWT>
```

Doğrulayıcı gelen her disclosure'ın özetini hesaplar ve `_sd` listesinde arar. Bulursa alanı
içeriğe yerleştirir. Listede olmayan bir disclosure gelirse sunum reddedilir: cüzdan, düzenleyicinin
imzalamadığı bir alanı ekleyemez.

**Key Binding JWT (KB-JWT):** Cüzdan sunumu kendi özel anahtarıyla imzalar. Bu anahtarın açık yarısı
kimlik bilgisinin `cnf.jwk` alanındadır. KB-JWT'de üç değer var:

- `aud`: sunumun hangi doğrulayıcıya yapıldığı;
- `nonce`: doğrulayıcının o istek için ürettiği tek kullanımlık değer;
- `sd_hash`: gösterilen SD-JWT'nin özeti, sondaki `~` dahil.

Sonuç: çalınan bir sunum başka bir doğrulayıcıda ya da başka bir istekte kullanılamaz.

## 3. Tasarım kararları

| Karar | Neden |
|---|---|
| Yalnız ES256 | HAIP 1.0 ES256'yı zorunlu tutar. Algoritma başlıktan okunur ama yalnız `ES256` kabul edilir; `none` ya da `HS256` ile imza atlatılamaz |
| İmza biçimi IEEE P1363 (r‖s, 64 bayt) | JWS bu biçimi ister. .NET'in `ECDsa.SignData` varsayılanı zaten bu; DER'e çevirmek gerekmez |
| Özet, base64url metnin özeti | RFC 9901 §4.2.3: özet çözülmüş JSON'un değil, disclosure metninin ASCII baytlarının özetidir. Testte aynı JSON'un boşluksuz yazımının farklı özet verdiği gösteriliyor |
| `_sd` listesi sıralı ve sahte özetli | Sıralama, alanların eklenme sırasını gizler. Sahte (decoy) özetler gizli alan sayısını gizler |
| JWK'da `d` varsa ret | Kanıt JWT'sinin başlığındaki anahtar özel anahtar içeremez (OpenID4VCI Ek F.4) |
| Status list için `ZLibStream` | Taslak zlib (RFC 1950) ister. `DeflateStream` ham DEFLATE (RFC 1951) üretir, başlığı ve sağlama toplamı yoktur; doğrulayıcılar onu çözemez |
| Çözülen liste için 16 MB sınırı | Küçük bir sıkıştırılmış veri çok büyük bir diziye açılabilir (sıkıştırma bombası) |

## 4. Testler

Testlerin bir kısmı spesifikasyonların kendi test vektörleriyle koşar. Vektörler
`SpecVectors.cs` dosyasında, satır kırılmaları kaldırılmış hâlde duruyor.

| Test | Ne kanıtlıyor? |
|---|---|
| `Digest_matches_rfc_9901` (7 vektör) | Disclosure özeti RFC 9901 §4.2 ve §5.1'deki değerlerle aynı |
| `Rfc_9901_presentation_verifies_and_shows_only_disclosed_claims` | RFC 9901 §5.2 sunumu: düzenleyici imzası, KB-JWT, `sd_hash`; gösterilmeyen dizi elemanı ("DE") sonuçtan çıkıyor |
| `Oid4vci_issued_credential_vector_verifies_as_sd_jwt_vc` | OpenID4VCI 1.0 Ek A.3.4'teki `dc+sd-jwt` kimlik bilgisi: 9 disclosure, imza doğru |
| `Oid4vci_proof_vector_verifies_with_its_header_jwk` | OpenID4VCI Ek F.1'deki kanıt JWT'si başlığındaki anahtarla doğrulanıyor |
| `Large_vector_has_exactly_the_listed_revocations` | Status List Ek C.1: 2^20 girdilik listede tam olarak 11 iptal |
| `Holder_cannot_add_a_claim_the_issuer_did_not_sign` | Cüzdanın uydurduğu disclosure `unreferenced_disclosure` ile reddediliyor |
| `Presentation_for_another_request_is_rejected` | Eski nonce'la yapılan sunum `invalid_nonce` |
| `Presentation_for_another_verifier_is_rejected` | Başka doğrulayıcıya yapılan sunum `invalid_audience` |
| `Key_binding_signed_with_another_key_is_rejected` | Kimlik bilgisini çalan, cüzdan anahtarı olmadan sunum yapamıyor |

Çalıştırma:

```powershell
dotnet test tests/DijitalKimlik.UnitTests --filter "FullyQualifiedName~Crypto"
```

Sonuç (23.10.2026, Linux konteyneri, .NET SDK 10.0.401): 74 birim testinin hepsi geçti; 39'u bu
fazın testi. Süre 153 ms.

## 5. Çıkan sorunlar ve dersler

- **Vektörü elle kopyalamak hata üretir.** RFC'deki uzun örnekler satır satır kırılmış. Elle
  birleştirmek yerine bir betik vektörleri çıkardı ve biçimini denetledi. Betik, sayıyı da denetledi:
  OpenID4VCI Ek A.3.4 örneğinde 9 disclosure var. Sayı önce yanlış varsayılmıştı; betik yakaladı.
- **Sıkıştırılmış çıktı tek değildir.** Aynı bayt dizisi farklı zlib çıktıları verebilir. Testler
  `lst` metnini değil, çözülmüş baytları karşılaştırır.
- **"Gönderilmedi" ile "yok" aynı değil.** Dizi elemanında gösterilmeyen değer sonuçtan çıkarılır;
  nesnede gösterilmeyen alan yalnız eklenmez. İkisi farklı kod yolu ister (`DisclosureProcessor`).

## 6. Alternatifler

| Seçenek | Artısı | Eksisi |
|---|---|---|
| Kendi SD-JWT katmanı, .NET kripto API'leri (seçilen) | Dış bağımlılık yok; her adım spesifikasyondaki adıma karşılık geliyor; vektörlerle doğrulanıyor | Bakımı bizde. Yalnız ES256 ve üst düzey alan seçimi var |
| JWS için `Microsoft.IdentityModel.JsonWebTokens`, SD-JWT katmanı yine bizde | Olgun bir JWS/JWT kütüphanesi; anahtar döndürme ve JWKS desteği | SD-JWT'yi bilmiyor; disclosure ve KB-JWT yine elle yazılır |
| Üçüncü taraf SD-JWT kütüphanesi | Daha az kod | Kamu kurumunda tedarik zinciri riski: bakımcı, lisans ve güvenlik güncellemesi denetlenmeli. Spesifikasyon yeni (RFC 9901 Kasım 2025); kütüphaneler geride kalabiliyor |

Kripto ilkelleri (ECDSA, SHA-256, zlib) hiçbir seçenekte elle yazılmaz; hepsi platformun kendisinden
gelir. Elle yazılan yalnız biçim katmanıdır: hangi baytın imzalanacağı, özetin neyin üzerinden
alınacağı.

## 7. Terimler

| Terim | Ne demek? |
|---|---|
| JWS | İmzalı JSON. Üç parçalı: başlık, içerik, imza |
| ES256 | P-256 eğrisi üzerinde ECDSA imzası, SHA-256 ile |
| JWK | Bir anahtarın JSON gösterimi |
| JWK parmak izi (RFC 7638) | Anahtarın zorunlu alanlarının sabit sırayla yazılıp özetlenmesi; aynı anahtar hep aynı izi verir |
| SD-JWT | Alanları özetle gizlenmiş, sahibinin seçerek gösterebildiği imzalı JWT |
| Disclosure | Gizli bir alanın açık hâli: `[salt, ad, değer]` |
| Salt | Özetin tahmin edilmesini engelleyen rastgele değer |
| Decoy (sahte özet) | Gizli alan sayısını gizlemek için eklenen, hiçbir alana karşılık gelmeyen özet |
| KB-JWT | Cüzdanın sunumu kendi anahtarıyla imzaladığı JWT; sunumu doğrulayıcıya ve isteğe bağlar |
| `cnf` | Kimlik bilgisinin bağlı olduğu cüzdan anahtarı (RFC 7800) |
| `vct` | Kimlik bilgisinin türü (ör. ikamet izni) |
| Token Status List | Kimlik bilgilerinin durumunu sıkıştırılmış bit dizisi olarak yayımlayan liste |
| zlib | DEFLATE sıkıştırmasını başlık ve sağlama toplamıyla saran biçim (RFC 1950) |
