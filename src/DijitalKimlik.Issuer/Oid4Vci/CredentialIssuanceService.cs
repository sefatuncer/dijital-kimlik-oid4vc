using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Crypto.SelectiveDisclosure;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>İkamet izninden SD-JWT VC üretir ve kurum tarafındaki kaydını (<see cref="IssuedCredential"/>) açar.</summary>
public sealed class CredentialIssuanceService(
    IssuerDbContext db,
    IssuerSigningKey signingKey,
    IOptions<IssuerOptions> options,
    TimeProvider clock,
    ILogger<CredentialIssuanceService> logger)
{
    /// <summary>Metadata'daki <c>credential_configurations_supported</c> anahtarı.</summary>
    public const string ConfigurationId = "DemoResidencePermit";

    public const string Vct = "urn:demo-goc-kurumu:ikamet-izni:1";

    /// <summary>Gizli alan sayısını belli etmemek için eklenen sahte özet sayısı.</summary>
    public const int DecoyDigests = 3;

    private const int MaxIndexAttempts = 10;

    public async Task<string> IssueAsync(AccessTokenGrant grant, HolderKey holder, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var permit = await db.ResidencePermits
            .Include(p => p.Foreigner)
            .SingleOrDefaultAsync(p => p.Id == grant.PermitId, cancellationToken);

        // Teklif verildikten sonra izin iptal edilmiş olabilir: karar şimdiki duruma göre verilir.
        if (permit is null || !permit.IsValidOn(today))
        {
            throw new Oid4VciException("credential_request_denied", "İkamet izni geçerli değil.");
        }

        var statusList = await db.StatusLists.OrderBy(l => l.Id).FirstAsync(cancellationToken);

        // Kimlik bilgisi iznin son gününün sonunda (23:59:59 UTC) geçersizleşir. Ertesi günün 00:00'ı
        // seçilseydi cüzdan ve doğrulayıcı bitiş tarihini bir gün sonrası olarak gösterirdi.
        var expiresAt = new DateTimeOffset(permit.ValidUntil.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

        // Sıra numarası rastgele seçilir; (StatusListId, StatusListIndex) benzersiz indeksi çakışmayı
        // yakalar. 20.000 kayıtta doluluk %15'tir (20.000 / 131.072); on denemenin hepsinin dolu bir
        // sıraya düşme olasılığı 0,15^10, yaklaşık 6 milyarda bir.
        IssuedCredential? record = null;
        for (var attempt = 1; record is null; attempt++)
        {
            var candidate = new IssuedCredential(
                "urn:uuid:" + Guid.NewGuid(),
                permit,
                Vct,
                holder.Thumbprint,
                statusList.Id,
                RandomNumberGenerator.GetInt32(statusList.Capacity),
                now,
                expiresAt);
            db.IssuedCredentials.Add(candidate);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                record = candidate;
            }
            catch (UniqueConstraintException) when (attempt < MaxIndexAttempts)
            {
                db.Entry(candidate).State = EntityState.Detached;
            }
        }

        var identifier = options.Value.Identifier;
        var header = new JsonObject { ["typ"] = SdJwtVc.Type, ["kid"] = signingKey.KeyId };
        var plain = new JsonObject
        {
            ["iss"] = identifier,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["vct"] = Vct,
            ["cnf"] = new JsonObject { ["jwk"] = holder.Jwk.DeepClone() },
            ["status"] = new JsonObject
            {
                ["status_list"] = new JsonObject
                {
                    ["idx"] = record.StatusListIndex,
                    ["uri"] = StatusListService.UriFor(identifier, record.StatusListId),
                },
            },
        };

        var credential = signingKey.Use(key => SdJwt.Issue(header, plain, DisclosableClaims(permit, today), key, DecoyDigests));

        // Günlüğe kişisel veri yazılmaz: kimlik numarası ya da ad değil, kaydın kendi kimliği.
        logger.LogInformation("Kimlik bilgisi verildi: {CredentialId}, status list {StatusListId}/{Index}",
            record.CredentialId, record.StatusListId, record.StatusListIndex);
        return credential;
    }

    /// <summary>
    /// Cüzdanın doğrulayıcıya tek tek açıp açmamayı seçebildiği alanlar. <c>age_over_18</c> türetilmiş
    /// bir alandır: "18 yaşından büyük mü?" sorusu doğum tarihi açılmadan cevaplanır.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, JsonNode?>> DisclosableClaims(ResidencePermit permit, DateOnly today)
    {
        var person = permit.Foreigner;
        return new Dictionary<string, JsonNode?>
        {
            ["given_name"] = person.GivenName,
            ["family_name"] = person.FamilyName,
            ["birthdate"] = person.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["nationality"] = person.Nationality,
            ["identity_number"] = person.IdentityNumber.Value,
            ["permit_number"] = permit.PermitNumber,
            ["permit_type"] = PermitTypeCode(permit.Type),
            ["valid_from"] = permit.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["valid_until"] = permit.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["age_over_18"] = person.AgeOn(today) >= 18,
        };
    }

    /// <summary>Kimlik bilgisindeki değer sabit bir koddur; ekranda gösterilecek ad cüzdanın işidir.</summary>
    public static string PermitTypeCode(PermitType type) => type switch
    {
        PermitType.ShortTerm => "short_term",
        PermitType.Family => "family",
        PermitType.Student => "student",
        PermitType.LongTerm => "long_term",
        PermitType.Humanitarian => "humanitarian",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
