using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Domain.StatusLists;
using DijitalKimlik.IntegrationTests.Infrastructure;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.IntegrationTests;

/// <summary>Function, trigger, temporal table ve en az yetki: veritabanı tarafındaki mantık.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ProgrammabilityTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Scalar_and_inline_functions_agree_with_the_domain_algorithm()
    {
        var inputs = Enumerable.Range(0, 300)
            .Select(_ => RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D9", CultureInfo.InvariantCulture))
            .ToList();
        var json = JsonSerializer.Serialize(inputs);

        // Tek sorgu, 300 satır: liste JSON parametresi olarak gider, OPENJSON satırlara açar.
        // Inline TVF, CROSS APPLY ile her satıra uygulanır.
        await using var db = sql.CreateContext();
        var rows = await db.Database.SqlQuery<CheckDigitRow>($"""
            SELECT j.[value] AS FirstNine,
                   issuer.ufn_IdentityNumberCheckDigits(j.[value]) AS Scalar,
                   f.CheckDigits AS Inline
            FROM OPENJSON({json}) AS j
            CROSS APPLY issuer.itvf_IdentityNumberCheckDigits(j.[value]) AS f
            """).ToListAsync();

        Assert.Equal(inputs.Count, rows.Count);
        Assert.All(rows, r =>
        {
            var expected = ForeignerIdentityNumber.ComputeCheckDigits(r.FirstNine);
            Assert.Equal(expected, r.Scalar);
            Assert.Equal(expected, r.Inline);
        });
    }

    [Theory]
    [InlineData("99ABC4567")]
    [InlineData("12345")]
    public async Task Functions_return_null_for_invalid_input(string firstNine)
    {
        await using var db = sql.CreateContext();
        var row = (await db.Database.SqlQuery<CheckDigitRow>($"""
            SELECT {firstNine} AS FirstNine,
                   issuer.ufn_IdentityNumberCheckDigits({firstNine}) AS Scalar,
                   f.CheckDigits AS Inline
            FROM issuer.itvf_IdentityNumberCheckDigits({firstNine}) AS f
            """).ToListAsync()).Single();

        Assert.Null(row.Scalar);
        Assert.Null(row.Inline);
    }

    [Fact]
    public async Task Status_trigger_writes_one_audit_row_per_changed_row_in_a_multi_row_update()
    {
        var credentials = await CreateCredentialsAsync(3);
        var ids = credentials.Select(c => c.Id).ToArray();
        var keys = credentials.Select(c => c.CredentialId).ToArray();

        await using var db = sql.CreateContext();

        // Kullanıcı adı bağlantıya yazılır, trigger SESSION_CONTEXT'ten okur. Bağlantı açık tutulur:
        // havuza dönen bağlantı sıfırlanınca (sp_reset_connection) bu değer silinir.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlAsync($"EXEC sp_set_session_context @key = N'UserName', @value = {"memur.test"}");

        // Tek UPDATE, üç satır: trigger bir kez çalışır, inserted ve deleted üçer satır içerir.
        var changed = await db.IssuedCredentials
            .Where(c => ids.Contains(c.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CredentialStatus.Suspended)
                .SetProperty(c => c.StatusReason, "Toplu inceleme"));
        Assert.Equal(3, changed);

        // Durumu değiştirmeyen güncelleme: UPDATE(Status) doğru olsa da eski ve yeni değer aynı.
        await db.IssuedCredentials
            .Where(c => ids.Contains(c.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CredentialStatus.Suspended));

        var audit = await db.AuditLog.AsNoTracking().Where(a => keys.Contains(a.RecordKey)).ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a =>
        {
            Assert.Equal("STATUS", a.Action);
            Assert.Equal("memur.test", a.ChangedBy);
            Assert.Equal("""{"status":1}""", a.OldValue);
            Assert.Equal("""{"status":2,"reason":"Toplu inceleme"}""", a.NewValue);
        });
    }

    [Fact]
    public async Task Ef_core_can_save_to_the_triggered_table_because_of_has_trigger()
    {
        var (id, credentialId) = (await CreateCredentialsAsync(1))[0];

        await using (var db = sql.CreateContext())
        {
            var credential = await db.IssuedCredentials.SingleAsync(c => c.Id == id);
            var before = credential.RowVersion.ToArray();

            credential.Revoke("İkamet izni iptal edildi", TestEntities.Now);
            await db.SaveChangesAsync();

            // HasTrigger olmasaydı SaveChanges "OUTPUT clause without INTO" hatası verirdi. Şimdi EF
            // yeni rowversion'ı ayrı bir SELECT ile okuyor.
            Assert.NotEqual(before, credential.RowVersion);
        }

        await using var check = sql.CreateContext();
        var entry = await check.AuditLog.AsNoTracking().SingleAsync(a => a.RecordKey == credentialId);
        Assert.Equal("""{"status":3,"reason":"İkamet izni iptal edildi"}""", entry.NewValue);
    }

    [Fact]
    public async Task Temporal_table_returns_the_permit_as_it_was_before_cancellation()
    {
        var foreigner = TestEntities.NewForeigner();
        var permit = foreigner.GrantPermit(
            TestEntities.NewPermitNumber(), PermitType.Family, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        await using (var db = sql.CreateContext())
        {
            db.Foreigners.Add(foreigner);
            await db.SaveChangesAsync();
        }

        await using (var db = sql.CreateContext())
        {
            var loaded = await db.ResidencePermits.SingleAsync(p => p.Id == permit.Id);
            loaded.Cancel("Adres beyanı gerçeğe aykırı");
            await db.SaveChangesAsync();
        }

        // Kod hiçbir geçmiş kaydı yazmadı: eski satırı SQL Server ResidencePermitsHistory'ye taşıdı.
        await using var read = sql.CreateContext();
        var versions = await read.ResidencePermits
            .TemporalAll()
            .Where(p => p.Id == permit.Id)
            .OrderBy(p => EF.Property<DateTime>(p, "PeriodStart"))
            .Select(p => new { p.Status, From = EF.Property<DateTime>(p, "PeriodStart") })
            .ToListAsync();
        Assert.Equal(new[] { PermitStatus.Active, PermitStatus.Cancelled }, versions.Select(v => v.Status));

        var asItWas = await read.ResidencePermits
            .TemporalAsOf(versions[0].From)
            .AsNoTracking()
            .SingleAsync(p => p.Id == permit.Id);
        Assert.Equal(PermitStatus.Active, asItWas.Status);
    }

    [Fact]
    public async Task App_user_cannot_write_audit_directly_but_the_trigger_can_through_ownership_chaining()
    {
        // Gerçek betik (db/00-uygulama-kullanicilari.sql) test veritabanına uygulanır.
        var password = "Dk-" + RandomNumberGenerator.GetHexString(16) + "aA1";
        var database = new SqlConnectionStringBuilder(sql.ConnectionString).InitialCatalog;
        await SqlScript.RunAsync(
            sql.ConnectionString,
            SqlScript.RepositoryFile("db", "00-uygulama-kullanicilari.sql"),
            new Dictionary<string, string> { ["IssuerAppPassword"] = password, ["DatabaseName"] = database });

        var (id, credentialId) = (await CreateCredentialsAsync(1))[0];
        var appConnectionString = new SqlConnectionStringBuilder(sql.ConnectionString)
        {
            UserID = "dk_issuer_app",
            Password = password,
        }.ConnectionString;

        await using var app = new SqlConnection(appConnectionString);
        await app.OpenAsync();

        // 1) Denetim tablosuna doğrudan yazamaz: 229 = izin reddedildi.
        var insert = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(app,
            "INSERT INTO audit.AuditLog (TableName, RecordKey, Action, ChangedBy) VALUES (N'x', N'y', N'z', N'sahte')"));
        Assert.Equal(229, insert.Number);

        // 2) Tablo silemez: 3701 = "yok ya da izniniz yok".
        var drop = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(app, "DROP TABLE issuer.StatusLists"));
        Assert.Equal(3701, drop.Number);

        // 3) Ama durumu güncelleyebilir; trigger audit'e yazar. Trigger ve audit tablosu aynı sahibe
        // (dbo) ait olduğu için zincir kopmaz ve audit.AuditLog üzerinde yetki kontrolü yapılmaz.
        await using (var update = new SqlCommand("UPDATE issuer.IssuedCredentials SET Status = 3 WHERE Id = @Id", app))
        {
            update.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await using var select = new SqlCommand("SELECT ChangedBy FROM audit.AuditLog WHERE RecordKey = @Key", app);
        select.Parameters.Add("@Key", SqlDbType.NVarChar, 64).Value = credentialId;
        Assert.Equal("dk_issuer_app", (string?)await select.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sqlText)
    {
        await using var command = new SqlCommand(sqlText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<(int Id, string CredentialId)>> CreateCredentialsAsync(int count)
    {
        await using var db = sql.CreateContext();
        var list = new StatusList(1024, TestEntities.Now);
        var foreigner = TestEntities.NewForeigner();
        var permit = foreigner.GrantPermit(
            TestEntities.NewPermitNumber(), PermitType.ShortTerm, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        db.StatusLists.Add(list);
        db.Foreigners.Add(foreigner);
        await db.SaveChangesAsync();

        var credentials = Enumerable.Range(0, count)
            .Select(i => new IssuedCredential(
                $"urn:uuid:{Guid.NewGuid()}", permit, "urn:demo-goc:residence-permit:1", "holder-jkt",
                list.Id, statusListIndex: i, issuedAt: TestEntities.Now, expiresAt: TestEntities.Now.AddYears(1)))
            .ToList();
        db.IssuedCredentials.AddRange(credentials);
        await db.SaveChangesAsync();

        return credentials.Select(c => (c.Id, c.CredentialId)).ToList();
    }

    private sealed class CheckDigitRow
    {
        public string FirstNine { get; set; } = string.Empty;

        public string? Scalar { get; set; }

        public string? Inline { get; set; }
    }
}
