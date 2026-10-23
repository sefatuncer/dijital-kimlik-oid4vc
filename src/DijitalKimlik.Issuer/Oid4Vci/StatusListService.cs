using System.Globalization;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.StatusLists;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.StatusLists;
using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>
/// İmzalı durum listesi (Token Status List, <c>typ: statuslist+jwt</c>). Doğrulayıcı kimlik bilgisindeki
/// <c>status.status_list.uri</c> adresinden bunu indirir ve <c>idx</c> sırasındaki 2 bite bakar.
/// </summary>
/// <remarks>
/// Liste her istekte veritabanından kurulur: memur iptal edince doğrulayıcı bunu bir sonraki
/// indirmede görür. Yük artınca alternatif, Worker'ın listeyi periyodik imzalayıp
/// <c>StatusList.PublishedToken</c>'a yazması ve önünde bir CDN'in durmasıdır. O durumda iptalin
/// görünme süresi <c>ttl</c> kadar gecikir.
/// </remarks>
public sealed class StatusListService(IssuerDbContext db, IssuerSigningKey signingKey, IOptions<IssuerOptions> options, TimeProvider clock)
{
    public const string Type = "statuslist+jwt";

    public const string MediaType = "application/statuslist+jwt";

    /// <summary>Doğrulayıcının listeyi yeniden indirmeden önce bekleyebileceği süre.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(1);

    public static string UriFor(string issuerIdentifier, int statusListId) =>
        issuerIdentifier + "/status-lists/" + statusListId.ToString(CultureInfo.InvariantCulture);

    public async Task<string?> BuildTokenAsync(int statusListId, CancellationToken cancellationToken)
    {
        var capacity = await db.StatusLists
            .Where(l => l.Id == statusListId)
            .Select(l => (int?)l.Capacity)
            .SingleOrDefaultAsync(cancellationToken);
        if (capacity is null)
        {
            return null;
        }

        // Yalnız etkin olmayanlar okunur: liste sıfırlarla başlar, sıfır "geçerli" demektir.
        var entries = await db.IssuedCredentials
            .Where(c => c.StatusListId == statusListId && c.Status != CredentialStatus.Active)
            .Select(c => new { c.StatusListIndex, c.Status })
            .ToListAsync(cancellationToken);

        var list = new TokenStatusList(bits: StatusList.BitsPerEntry, size: capacity.Value);
        foreach (var entry in entries)
        {
            list[entry.StatusListIndex] = IssuedCredential.ToStatusListValue(entry.Status);
        }

        var now = clock.GetUtcNow();
        var payload = new JsonObject
        {
            // sub, kimlik bilgisindeki uri ile birebir aynı olmalı (Status List §5.1).
            ["sub"] = UriFor(options.Value.Identifier, statusListId),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["ttl"] = (long)TimeToLive.TotalSeconds,
            ["status_list"] = new JsonObject { ["bits"] = list.Bits, ["lst"] = list.Encode() },
        };
        return signingKey.Sign(new JsonObject { ["typ"] = Type, ["kid"] = signingKey.KeyId }, payload);
    }
}
