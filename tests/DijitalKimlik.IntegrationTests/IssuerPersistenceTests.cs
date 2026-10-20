using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Domain.StatusLists;
using DijitalKimlik.IntegrationTests.Infrastructure;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class IssuerPersistenceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Now = TestEntities.Now;

    [Fact]
    public async Task Value_object_round_trips_and_permits_load_with_include()
    {
        var foreigner = TestEntities.NewForeigner();
        foreigner.GrantPermit(TestEntities.NewPermitNumber(), PermitType.Student, new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));
        await using (var db = sql.CreateContext())
        {
            db.Foreigners.Add(foreigner);
            await db.SaveChangesAsync();
        }

        // Yeni bir bağlam: change tracker'daki nesne değil, veritabanından okunan nesne kontrol edilir.
        await using var read = sql.CreateContext();
        var loaded = await read.Foreigners
            .Include(f => f.Permits)
            .SingleAsync(f => f.IdentityNumber == foreigner.IdentityNumber);

        Assert.Equal(foreigner.IdentityNumber, loaded.IdentityNumber);
        Assert.Single(loaded.Permits);
        Assert.Equal(PermitType.Student, loaded.Permits.Single().Type);
    }

    [Fact]
    public async Task Duplicate_identity_number_is_translated_to_unique_constraint_exception()
    {
        var first9 = TestEntities.NewFirst9();
        await using (var db = sql.CreateContext())
        {
            db.Foreigners.Add(TestEntities.NewForeigner(first9));
            await db.SaveChangesAsync();
        }

        // İkinci istek ön kontrolü atlamış gibi doğrudan ekler: unique index yakalamalı.
        await using var second = sql.CreateContext();
        second.Foreigners.Add(TestEntities.NewForeigner(first9));

        var ex = await Assert.ThrowsAsync<UniqueConstraintException>(() => second.SaveChangesAsync());
        Assert.IsType<DbUpdateException>(ex.InnerException);
    }

    [Fact]
    public async Task Concurrent_update_of_same_credential_raises_concurrency_exception()
    {
        var credentialId = await CreateCredentialAsync();

        // İki memur aynı kaydı aynı anda açtı.
        await using var officer1 = sql.CreateContext();
        await using var officer2 = sql.CreateContext();
        var c1 = await officer1.IssuedCredentials.SingleAsync(c => c.Id == credentialId);
        var c2 = await officer2.IssuedCredentials.SingleAsync(c => c.Id == credentialId);

        c1.Suspend("Belge incelemesi", Now);
        await officer1.SaveChangesAsync();

        // İkinci memurun elindeki RowVersion eskidi: UPDATE ... WHERE RowVersion = @eski 0 satır etkiler.
        c2.Revoke("İkamet izni iptal edildi", Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => officer2.SaveChangesAsync());

        await using var check = sql.CreateContext();
        var final = await check.IssuedCredentials.AsNoTracking().SingleAsync(c => c.Id == credentialId);
        Assert.Equal(CredentialStatus.Suspended, final.Status);
    }

    [Fact]
    public async Task Check_constraint_rejects_invalid_period_even_from_raw_sql()
    {
        var foreigner = TestEntities.NewForeigner();
        await using var db = sql.CreateContext();
        db.Foreigners.Add(foreigner);
        await db.SaveChangesAsync();

        // Domain kuralını atlayan doğrudan SQL. ExecuteSqlInterpolated değerleri parametre olarak gönderir.
        var permitNumber = TestEntities.NewPermitNumber();
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO issuer.ResidencePermits (ForeignerId, PermitNumber, Type, ValidFrom, ValidUntil, Status)
            VALUES ({foreigner.Id}, {permitNumber}, 1, '2027-01-01', '2026-01-01', 1)
            """));

        Assert.Equal(547, ex.Number); // 547: constraint ihlali
    }

    private async Task<int> CreateCredentialAsync()
    {
        await using var db = sql.CreateContext();
        var list = new StatusList(1024, Now);
        var foreigner = TestEntities.NewForeigner();
        var permit = foreigner.GrantPermit(TestEntities.NewPermitNumber(), PermitType.ShortTerm, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        db.StatusLists.Add(list);
        db.Foreigners.Add(foreigner);
        await db.SaveChangesAsync();

        var credential = new IssuedCredential(
            $"urn:uuid:{Guid.NewGuid()}", permit, "urn:demo-goc:residence-permit:1", "holder-jkt",
            list.Id, statusListIndex: 7, issuedAt: Now, expiresAt: Now.AddYears(1));
        db.IssuedCredentials.Add(credential);
        await db.SaveChangesAsync();
        return credential.Id;
    }
}
