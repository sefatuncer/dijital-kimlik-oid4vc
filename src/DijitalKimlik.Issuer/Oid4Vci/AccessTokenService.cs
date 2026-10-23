using System.Globalization;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

public sealed record AccessTokenGrant(int PermitId, int OfferId);

/// <summary>
/// Token uç noktasının verdiği erişim token'ı: imzalı, kısa ömürlü bir JWT (RFC 9068, <c>typ: at+jwt</c>).
/// Durum sunucuda tutulmaz; token kendi geçerliliğini taşır. Cüzdan için token içeriği önemsizdir,
/// yalnız <c>Authorization: Bearer</c> başlığında geri gönderir.
/// </summary>
public sealed class AccessTokenService(IssuerSigningKey signingKey, IOptions<IssuerOptions> options, TimeProvider clock)
{
    public const string Type = "at+jwt";

    private const string SubjectPrefix = "permit:";

    public TimeSpan Lifetime => options.Value.AccessTokenLifetime;

    public string Issue(int permitId, int offerId)
    {
        var now = clock.GetUtcNow();
        var issuer = options.Value.Identifier;
        var payload = new JsonObject
        {
            ["iss"] = issuer,
            ["aud"] = issuer,
            ["sub"] = SubjectPrefix + permitId.ToString(CultureInfo.InvariantCulture),
            ["offer_id"] = offerId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        return signingKey.Sign(new JsonObject { ["typ"] = Type, ["kid"] = signingKey.KeyId }, payload);
    }

    public AccessTokenGrant? Validate(string token)
    {
        try
        {
            var jws = Jws.Parse(token);
            jws.RequireType(Type);
            signingKey.Use(key =>
            {
                jws.VerifySignature(key);
                return true;
            });

            var payload = jws.Payload;
            var issuer = options.Value.Identifier;
            var sub = JoseJson.GetString(payload, "sub");
            if (JoseJson.GetString(payload, "iss") != issuer
                || JoseJson.GetString(payload, "aud") != issuer
                || JoseJson.GetInt64(payload, "exp") is not { } exp
                || clock.GetUtcNow() >= DateTimeOffset.FromUnixTimeSeconds(exp)
                || JoseJson.GetInt64(payload, "offer_id") is not { } offerId
                || sub is null
                || !sub.StartsWith(SubjectPrefix, StringComparison.Ordinal)
                || !int.TryParse(sub.AsSpan(SubjectPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var permitId))
            {
                return null;
            }

            return new AccessTokenGrant(permitId, (int)offerId);
        }
        catch (CryptoValidationException)
        {
            return null;
        }
    }
}
