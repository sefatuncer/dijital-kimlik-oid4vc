# Dijital Kimlik: OpenID4VCI ve OpenID4VP ile dijital ikamet izni

[![CI](https://github.com/sefatuncer/dijital-kimlik-oid4vc/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/sefatuncer/dijital-kimlik-oid4vc/actions/workflows/ci.yml)

Bu depo, .NET 10 ile yazılmış bir **referans uygulama**. Hayali bir "Demo Göç Kurumu",
yabancılara dijital ikamet izni kimlik bilgisi (SD-JWT VC) veriyor. Yabancı bu bilgiyi web
cüzdanında saklıyor. Doğrulayıcı kurumlar yalnız ihtiyaç duydukları alanları istiyor ve doğruluyor.

> Gerçek bir kurumla ilişkisi yoktur. Kurum adı, kişi verileri ve anahtarlar hayalidir.

![Uçtan uca akış: memur teklif oluşturur, cüzdan kimlik bilgisini alır, otel 5 alanı, mağaza yalnız yaşı doğrular, iptalden sonra doğrulama düşer](docs/gorseller/akis/akis.gif)

Memur teklif oluşturur → cüzdan PIN'le kimlik bilgisini alır → otel yalnız 5 alanı, mağaza yalnız
"18 yaşından büyük" bilgisini doğrular → memur iptal edince aynı kimlik bilgisi doğrulanmaz.
Adım adım ekran görüntüleri, bitmiş fazlar, testler ve ölçümler: [Proje turu](docs/proje-turu.md).

## Bileşenler

| Bileşen | Görev |
|---|---|
| Issuer | OpenID4VCI uç noktaları ve memur portalı (ASP.NET Core MVC) |
| Wallet | Çok kullanıcılı web cüzdanı |
| Verifier | OpenID4VP uç noktaları ve doğrulayıcı portalı |
| LegacySoap | Eski kimlik sorgulama sistemini taklit eden SOAP servisi (CoreWCF) |
| Worker | Windows servisi: süre dolumu, iptal listesi (status list), temizlik |
| Gateway | YARP API gateway |

Mimari çok katmanlı: Domain, Application, Infrastructure, Crypto ve sunum katmanı.
Veritabanı SQL Server.

## Geliştirme ortamı

Gereken: .NET SDK 10.0.402 (`global.json`), Docker (SQL Server ve Redis için).

```powershell
# SDK kullanıcı klasörüne kuruluysa, oturumda etkinleştir
. .\scripts\ortam.ps1
dotnet build
dotnet test
```

## Belgeler

| Klasör | İçerik |
|---|---|
| `docs/proje-turu.md` | Projenin aşama aşama durumu: ne bitti, nasıl doğrulandı, sırada ne var |
| `docs/gelistirme/` | Her geliştirme adımının notu: amaç, tasarım kararları, komutlar, karşılaşılan sorunlar ve alternatifler |
| `docs/olcumler/` | Performans ölçümleri: yöntem, ham veri, sonuçlar ve yorum |

| Not | Konu |
|---|---|
| [00](docs/gelistirme/00-ortam-git-github.md) | Geliştirme ortamı, Git ve GitHub iş akışı |
| [01](docs/gelistirme/01-iskelet-mimari-domain.md) | Çözüm iskeleti, katmanlı mimari, domain modeli |
| [02](docs/gelistirme/02-veritabani-ef-core-eszamanlilik.md) | SQL Server, EF Core ve eşzamanlılık |
| [03](docs/gelistirme/03-programlanabilirlik-sp-function-trigger.md) | Stored procedure, function, trigger, temporal table, veritabanı dağıtımı |
| [04](docs/gelistirme/04-kripto-sd-jwt.md) | Kripto çekirdeği: JWS, SD-JWT VC, Token Status List |
| [05](docs/gelistirme/05-issuer-oid4vci.md) | Issuer: OpenID4VCI uç noktaları, memur portalı, durum listesi |
| [07](docs/gelistirme/07-cuzdan-dogrulayici.md) | Web cüzdanı ve OpenID4VP doğrulayıcı: seçerek paylaşma, sekiz denetim, uçtan uca testler |
| [Ölçüm 1](docs/olcumler/01-eszamanlilik-yontemleri.md) | Dört eşzamanlılık yönteminin yük altında karşılaştırması |
