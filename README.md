# Dijital Kimlik: OpenID4VCI ve OpenID4VP ile dijital ikamet izni

Bu depo, .NET 10 ile yazılmış bir **eğitim ve demo projesi**. Hayali bir "Demo Göç Kurumu",
yabancılara dijital ikamet izni kimlik bilgisi (SD-JWT VC) veriyor. Yabancı bu bilgiyi web
cüzdanında saklıyor. Doğrulayıcı kurumlar yalnız ihtiyaç duydukları alanları istiyor ve doğruluyor.

> Gerçek bir kurumla ilişkisi yoktur. Kurum adı, kişi verileri ve anahtarlar hayalidir.

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

Her geliştirme adımı `docs/bolumler/` altında ayrı bir bölüm olarak anlatılıyor: amaç, kavramlar,
komutlar, kod, çıkan sorunlar ve mülakat soruları.
