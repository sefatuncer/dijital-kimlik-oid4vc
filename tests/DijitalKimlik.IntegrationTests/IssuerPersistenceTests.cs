using System.Security.Cryptography;

using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Domain.StatusLists;
using DijitalKimlik.IntegrationTests.Infrastructure;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class IssuerPersistenceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Value_object_round_trips_and_permits_load_with_include()
    {
        var foreigner = NewForeigner();
        foreigner.GrantPermit(NewPermitNumber(), PermitType.Student, new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));
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
        var first9 = NewFirst9();
        await using (var db = sql.CreateContext())
        {
            db.Foreigners.Add(NewForeigner(first9));
            await db.SaveChangesAsync();
        }

        // İkinci istek ön kontrolü atlamış gibi doğrudan ekler: unique index yakalamalı.
        await using var second = sql.CreateContext();
        second.Foreigners.Add(NewForeigner(first9));

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
        var foreigner = NewForeigner();
        await using var db = sql.CreateContext();
        db.Foreigners.Add(foreigner);
        await db.SaveChangesAsync();

        // Domain kuralını atlayan doğrudan SQL. ExecuteSqlInterpolated değerleri parametre olarak gönderir.
        var permitNumber = NewPermitNumber();
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
        var foreigner = NewForeigner();
        var permit = foreigner.GrantPermit(NewPermitNumber(), PermitType.ShortTerm, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
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

    // Testler aynı veritabanını paylaşır: her test kendi benzersiz numaralarını üretir.
    private static string NewFirst9() => "99" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", System.Globalization.CultureInfo.InvariantCulture);

    private static string NewPermitNumber() => "IK-T-" + RandomNumberGenerator.GetHexString(10);

    private static Foreigner NewForeigner(string? first9 = null)
    {
        first9 ??= NewFirst9();
        var number = ForeignerIdentityNumber.Create(first9 + ForeignerIdentityNumber.ComputeCheckDigits(first9));
        return new Foreigner(number, "Test", "Kişi", new DateOnly(1990, 1, 1), "IRN", "T" + first9, Now);
    }
}
