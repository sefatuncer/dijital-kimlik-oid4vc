using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DijitalKimlik.Infrastructure.Persistence.Migrations;

/// <summary>
/// Veritabanı programlanabilirliği: function, stored procedure ve trigger.
/// </summary>
/// <remarks>
/// SQL metni migration'ın içinde sabit durur, ayrı bir .sql dosyasından okunmaz. Dosyadan okunsaydı
/// dosya ileride değiştiğinde bu eski migration da sessizce değişirdi; bir migration yazıldığı
/// günkü hâliyle donmalıdır. Her nesne ayrı <c>Sql</c> çağrısındadır, çünkü <c>CREATE OR ALTER</c>
/// bir batch'in ilk ifadesi olmalıdır.
/// <para>Her <c>CREATE</c> ayrıca <see cref="Exec"/> ile dinamik SQL'e sarılır. İdempotent betikte
/// (<c>migrations script --idempotent</c>) EF her migration'ı <c>IF NOT EXISTS (...) BEGIN ... END</c>
/// bloğuna koyar; <c>CREATE FUNCTION/PROCEDURE/TRIGGER</c> böyle bir bloğun içinde geçersizdir
/// ("must be the first statement in a query batch"). <c>EXEC</c> içindeki metin kendi batch'idir.</para>
/// </remarks>
public partial class Programmability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1a) Scalar function: kontrol hanelerini döngüyle hesaplar. Okunması kolaydır, ama bir
        // sorguda her satır için ayrı ayrı çağrılır.
        migrationBuilder.Sql(Exec("""
            CREATE OR ALTER FUNCTION issuer.ufn_IdentityNumberCheckDigits (@FirstNine char(9))
            RETURNS char(2)
            WITH SCHEMABINDING, RETURNS NULL ON NULL INPUT
            AS
            BEGIN
                -- char(9) kısa değeri boşlukla doldurur; boşluk da rakam olmadığı için reddedilir.
                IF @FirstNine LIKE '%[^0-9]%'
                    RETURN NULL;

                DECLARE @Odd int = 0, @Even int = 0, @Position int = 1;
                WHILE @Position <= 9
                BEGIN
                    IF @Position % 2 = 1
                        SET @Odd += CAST(SUBSTRING(@FirstNine, @Position, 1) AS int);
                    ELSE
                        SET @Even += CAST(SUBSTRING(@FirstNine, @Position, 1) AS int);
                    SET @Position += 1;
                END;

                -- T-SQL'de de % negatif sonuç verebilir: +10 ve tekrar mod ile 0-9 aralığına çekilir.
                DECLARE @Tenth int = (((@Odd * 7) - @Even) % 10 + 10) % 10;
                RETURN CONCAT(@Tenth, (@Odd + @Even + @Tenth) % 10);
            END;
            """));

        // 1b) Aynı hesap inline table-valued function olarak. Tek bir SELECT'tir; optimizer onu
        // çağıran sorgunun içine açar (view gibi), satır satır çağrı maliyeti yoktur.
        migrationBuilder.Sql(Exec("""
            CREATE OR ALTER FUNCTION issuer.itvf_IdentityNumberCheckDigits (@FirstNine char(9))
            RETURNS TABLE
            WITH SCHEMABINDING
            AS
            RETURN
                SELECT CheckDigits = CASE
                           WHEN @FirstNine IS NULL OR @FirstNine LIKE '%[^0-9]%' THEN NULL
                           ELSE CONCAT(t.Tenth, (s.Odd + s.Even + t.Tenth) % 10)
                       END
                FROM (VALUES (
                        TRY_CAST(SUBSTRING(@FirstNine, 1, 1) AS int) + TRY_CAST(SUBSTRING(@FirstNine, 3, 1) AS int)
                      + TRY_CAST(SUBSTRING(@FirstNine, 5, 1) AS int) + TRY_CAST(SUBSTRING(@FirstNine, 7, 1) AS int)
                      + TRY_CAST(SUBSTRING(@FirstNine, 9, 1) AS int),
                        TRY_CAST(SUBSTRING(@FirstNine, 2, 1) AS int) + TRY_CAST(SUBSTRING(@FirstNine, 4, 1) AS int)
                      + TRY_CAST(SUBSTRING(@FirstNine, 6, 1) AS int) + TRY_CAST(SUBSTRING(@FirstNine, 8, 1) AS int)
                     )) AS s (Odd, Even)
                CROSS APPLY (VALUES ((((s.Odd * 7) - s.Even) % 10 + 10) % 10)) AS t (Tenth);
            """));

        // 2) Stored procedure: teklifi tek komutta kullanır. C# tarafındaki iyimser, kötümser ve
        // atomik yöntemlerin dördüncü alternatifi. OUTPUT ... INTO, güncellenen satırın eski ve yeni
        // değerlerini aynı komutta verir: EF Core ExecuteUpdate'in veremediği bilgi.
        migrationBuilder.Sql(Exec("""
            CREATE OR ALTER PROCEDURE issuer.usp_RedeemOffer
                @PreAuthorizedCodeHash char(43),
                @TxCodeHash            char(43),
                @Now                   datetimeoffset,
                @MaxAttempts           int = 5
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                -- Sonuç kodları Domain'deki RedeemResult ile aynı:
                -- 0 Success, 1 WrongTxCode, 2 Locked, 3 Expired, 4 AlreadyRedeemed.
                DECLARE @Changed TABLE (Id int, ResidencePermitId int, NewStatus tinyint);

                -- Durum 1 = Pending, 2 = Redeemed, 4 = Locked. SET listesindeki bütün ifadeler satırın
                -- güncellemeden ÖNCEKİ değerlerini görür.
                UPDATE issuer.CredentialOffers
                SET Status = CASE
                                 WHEN TxCodeHash = @TxCodeHash THEN 2
                                 WHEN FailedTxCodeAttempts + 1 >= @MaxAttempts THEN 4
                                 ELSE Status
                             END,
                    RedeemedAt = CASE WHEN TxCodeHash = @TxCodeHash THEN @Now ELSE RedeemedAt END,
                    FailedTxCodeAttempts = CASE
                                               WHEN TxCodeHash = @TxCodeHash THEN FailedTxCodeAttempts
                                               ELSE FailedTxCodeAttempts + 1
                                           END
                OUTPUT inserted.Id, inserted.ResidencePermitId, inserted.Status
                    INTO @Changed (Id, ResidencePermitId, NewStatus)
                WHERE PreAuthorizedCodeHash = @PreAuthorizedCodeHash
                  AND Status = 1
                  AND ExpiresAt > @Now;

                IF EXISTS (SELECT 1 FROM @Changed)
                BEGIN
                    SELECT Result = CASE NewStatus WHEN 2 THEN 0 WHEN 4 THEN 2 ELSE 1 END,
                           OfferId = Id,
                           ResidencePermitId
                    FROM @Changed;
                    RETURN 0;
                END;

                -- Hiçbir satır değişmedi: teklif yok, kullanılmış, kilitli ya da süresi geçmiş.
                -- Kod bilinmiyorsa sonuç kümesi boş döner.
                SELECT Result = CASE Status WHEN 2 THEN 4 WHEN 4 THEN 2 ELSE 3 END,
                       OfferId = Id,
                       ResidencePermitId
                FROM issuer.CredentialOffers
                WHERE PreAuthorizedCodeHash = @PreAuthorizedCodeHash;
                RETURN 0;
            END;
            """));

        // 3) Trigger: kimlik bilgisinin durumu her değiştiğinde denetim kaydı yazar. Uygulama
        // kullanıcısının audit şemasına INSERT yetkisi yok; trigger ile audit tablosu aynı sahibe
        // (dbo) ait olduğundan ownership chaining sayesinde yazabilir. Kayıt uygulamayı atlayan
        // doğrudan SQL güncellemelerinde de tutulur.
        migrationBuilder.Sql(Exec("""
            CREATE OR ALTER TRIGGER issuer.trg_IssuedCredentials_StatusAudit
            ON issuer.IssuedCredentials
            AFTER UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;

                -- UPDATE(Status): sütun SET listesinde mi? Değerin değişip değişmediğini söylemez,
                -- o yüzden aşağıda eski ve yeni değer ayrıca karşılaştırılır.
                IF NOT UPDATE(Status)
                    RETURN;

                -- inserted/deleted birden çok satır içerebilir: tek satır varsayan trigger klasik hatadır.
                INSERT INTO audit.AuditLog (TableName, RecordKey, Action, OldValue, NewValue, ChangedBy)
                SELECT N'issuer.IssuedCredentials',
                       i.CredentialId,
                       N'STATUS',
                       (SELECT d.Status AS [status] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                       (SELECT i.Status AS [status], i.StatusReason AS [reason] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                       COALESCE(CAST(SESSION_CONTEXT(N'UserName') AS nvarchar(128)), ORIGINAL_LOGIN())
                FROM inserted AS i
                JOIN deleted AS d ON d.Id = i.Id
                WHERE i.Status <> d.Status;
            END;
            """));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS issuer.trg_IssuedCredentials_StatusAudit;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS issuer.usp_RedeemOffer;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS issuer.itvf_IdentityNumberCheckDigits;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS issuer.ufn_IdentityNumberCheckDigits;");
    }

    /// <summary>Metni <c>EXEC(N'...')</c> içine alır; tek tırnaklar (yorumlardakiler de) ikilenir.</summary>
    private static string Exec(string sql) => $"EXEC(N'{sql.Replace("'", "''", StringComparison.Ordinal)}');";
}
