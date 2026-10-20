/*
  Uygulama veritabanı kullanıcıları (en az yetki ilkesi).

  - Migration'ları yönetici hesabı uygular (DDL yetkisi gerekir).
  - Uygulama ayrı bir login ile bağlanır: yalnız kendi şemasında veri okur/yazar ve SP çalıştırır.
    Tablo silemez, şema değiştiremez. SQL injection olsa bile verebileceği zarar sınırlıdır.
  - Denetim şemasına yalnız okuma yetkisi var. Denetim kayıtlarını trigger yazar; trigger ile
    tablo aynı sahibe (dbo) ait olduğundan "ownership chaining" sayesinde ayrıca yetki gerekmez.

  Çalıştırma: sqlcmd -v IssuerAppPassword="..." -i 00-uygulama-kullanicilari.sql
*/
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'dk_issuer_app')
    CREATE LOGIN dk_issuer_app WITH PASSWORD = N'$(IssuerAppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = DijitalKimlik;
ELSE
    ALTER LOGIN dk_issuer_app WITH PASSWORD = N'$(IssuerAppPassword)';
GO

USE DijitalKimlik;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'dk_issuer_app')
    CREATE USER dk_issuer_app FOR LOGIN dk_issuer_app WITH DEFAULT_SCHEMA = issuer;
GO

GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::issuer TO dk_issuer_app;
GRANT SELECT ON SCHEMA::audit TO dk_issuer_app;
GO

PRINT N'dk_issuer_app hazır.';
