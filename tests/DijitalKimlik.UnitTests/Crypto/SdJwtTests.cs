using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;

namespace DijitalKimlik.UnitTests.Crypto;

public sealed class SdJwtTests : IDisposable
{
    private const string Verifier = "redirect_uri:http://localhost:5103/presentations/response";
    private const string Nonce = "n-0S6_WzA2Mj";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly KeyBindingRequirement Binding = new(Verifier, Nonce, TimeSpan.FromMinutes(5));

    private readonly ECDsa _issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _issuerKey.Dispose();
        _holderKey.Dispose();
    }

    [Fact]
    public void Rfc_9901_presentation_verifies_and_shows_only_disclosed_claims()
    {
        var sdJwt = SdJwt.Parse(SpecVectors.Rfc9901Presentation);
        using var issuerKey = SpecIssuerKey();

        var claims = sdJwt.VerifyAndProcess(issuerKey);
        using var holderKey = EcJwk.ToPublicKey((JsonObject)claims["cnf"]!["jwk"]!);
        KeyBindingJwt.Verify(sdJwt.KeyBindingJwt!, sdJwt.WithoutKeyBinding, holderKey, "https://verifier.example.org", "1234567890",
            DateTimeOffset.FromUnixTimeSeconds(1748537244), TimeSpan.FromMinutes(5));

        Assert.Equal("John", JoseJson.GetString(claims, "given_name"));
        Assert.Equal("Doe", JoseJson.GetString(claims, "family_name"));
        Assert.Equal("Anytown", claims["address"]!["locality"]!.GetValue<string>());
        // "DE" gösterilmediği için dizi elemanı sonuçtan çıkar; yalnız "US" kalır.
        Assert.Equal("[\"US\"]", JoseJson.Serialize(claims["nationalities"]!));
        Assert.False(claims.ContainsKey("_sd"));
        Assert.False(claims.ContainsKey("_sd_alg"));
    }

    [Fact]
    public void Oid4vci_issued_credential_vector_verifies_as_sd_jwt_vc()
    {
        using var issuerKey = SpecIssuerKey();

        var verified = SdJwtVc.Verify(SdJwt.Parse(SpecVectors.Oid4VciIssuedCredential), issuerKey,
            DateTimeOffset.FromUnixTimeSeconds(1683000000).AddDays(1), keyBinding: null);

        Assert.Equal("doc-signer-05-25-2022", JoseJson.GetString(verified.Header, "kid"));
        Assert.Equal("https://credentials.example.com/identity_credential", JoseJson.GetString(verified.Claims, "vct"));
        Assert.Equal("johndoe@example.com", JoseJson.GetString(verified.Claims, "email"));
        Assert.True(verified.Claims["is_over_65"]!.GetValue<bool>());
        Assert.Equal(9, verified.DisclosedClaimNames.Count);
    }

    [Fact]
    public void Wrong_typ_is_rejected_for_sd_jwt_vc()
    {
        using var issuerKey = SpecIssuerKey();

        // RFC 9901 örneğinin typ değeri example+sd-jwt; SD-JWT VC dc+sd-jwt ister.
        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(SpecVectors.Rfc9901Presentation), issuerKey, Now, Binding));
        Assert.Equal("invalid_typ", ex.Code);
    }

    [Fact]
    public void Issued_credential_hides_every_disclosable_claim_behind_a_digest()
    {
        var credential = Issue();

        var payload = SdJwt.Parse(credential).IssuerJwt.Payload;

        Assert.False(payload.ContainsKey("given_name"));
        Assert.False(payload.ContainsKey("birthdate"));
        Assert.Equal(5 + 3, payload["_sd"]!.AsArray().Count);
        Assert.EndsWith("~", credential, StringComparison.Ordinal);
    }

    [Fact]
    public void Holder_presents_only_selected_claims_and_verifier_accepts()
    {
        var presentation = SdJwtVc.Present(Issue(), ["given_name", "permit_type"], _holderKey, Verifier, Nonce, Now);

        var verified = SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now.AddSeconds(5), Binding);

        Assert.Equal("Amina", JoseJson.GetString(verified.Claims, "given_name"));
        Assert.Equal("ShortTerm", JoseJson.GetString(verified.Claims, "permit_type"));
        Assert.False(verified.Claims.ContainsKey("family_name"));
        Assert.False(verified.Claims.ContainsKey("birthdate"));
        string[] disclosed = ["given_name", "permit_type"];
        Assert.Equal(disclosed, verified.DisclosedClaimNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Presentation_for_another_request_is_rejected()
    {
        var presentation = SdJwtVc.Present(Issue(), ["given_name"], _holderKey, Verifier, "eski-nonce", Now);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, Binding));
        Assert.Equal("invalid_nonce", ex.Code);
    }

    [Fact]
    public void Presentation_for_another_verifier_is_rejected()
    {
        var presentation = SdJwtVc.Present(Issue(), ["given_name"], _holderKey, "redirect_uri:https://baska.example/cb", Nonce, Now);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, Binding));
        Assert.Equal("invalid_audience", ex.Code);
    }

    [Fact]
    public void Old_key_binding_jwt_is_rejected()
    {
        var presentation = SdJwtVc.Present(Issue(), ["given_name"], _holderKey, Verifier, Nonce, Now.AddMinutes(-10));

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, Binding));
        Assert.Equal("kb_jwt_expired", ex.Code);
    }

    [Fact]
    public void Presentation_without_key_binding_is_rejected_when_binding_is_required()
    {
        var withoutKb = SdJwt.Combine(SdJwt.Parse(Issue()).IssuerJwt.Compact, []);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(withoutKb), _issuerKey, Now, Binding));
        Assert.Equal("missing_kb_jwt", ex.Code);
    }

    [Fact]
    public void Key_binding_signed_with_another_key_is_rejected()
    {
        using var thief = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var presentation = SdJwtVc.Present(Issue(), ["given_name"], thief, Verifier, Nonce, Now);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, Binding));
        Assert.Equal("invalid_signature", ex.Code);
    }

    [Fact]
    public void Holder_cannot_add_a_claim_the_issuer_did_not_sign()
    {
        var parsed = SdJwt.Parse(Issue());
        var forged = Disclosure.ForProperty("permit_type", "LongTerm");
        var sdJwt = SdJwt.Combine(parsed.IssuerJwt.Compact, [forged]);
        var presentation = sdJwt + KeyBindingJwt.Create(sdJwt, _holderKey, Verifier, Nonce, Now);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, Binding));
        Assert.Equal("unreferenced_disclosure", ex.Code);
    }

    [Fact]
    public void Same_disclosure_twice_is_rejected()
    {
        var parsed = SdJwt.Parse(Issue());
        var twice = SdJwt.Combine(parsed.IssuerJwt.Compact, [parsed.Disclosures[0], parsed.Disclosures[0]]);

        var ex = Assert.Throws<CryptoValidationException>(() => SdJwtVc.Verify(SdJwt.Parse(twice), _issuerKey, Now, keyBinding: null));
        Assert.Equal("duplicate_disclosure", ex.Code);
    }

    [Fact]
    public void Credential_signed_by_another_issuer_is_rejected()
    {
        using var otherIssuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(Issue()), otherIssuer, Now, keyBinding: null));
        Assert.Equal("invalid_signature", ex.Code);
    }

    [Fact]
    public void Expired_credential_is_rejected()
    {
        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(Issue()), _issuerKey, Now.AddYears(2), keyBinding: null));
        Assert.Equal("credential_expired", ex.Code);
    }

    [Fact]
    public void Credential_received_with_key_binding_is_rejected_by_the_holder()
    {
        var presentation = SdJwtVc.Present(Issue(), ["given_name"], _holderKey, Verifier, Nonce, Now);

        var ex = Assert.Throws<CryptoValidationException>(() =>
            SdJwtVc.Verify(SdJwt.Parse(presentation), _issuerKey, Now, keyBinding: null));
        Assert.Equal("unexpected_kb_jwt", ex.Code);
    }

    private static ECDsa SpecIssuerKey() => EcJwk.ToPublicKey(new JsonObject
    {
        ["kty"] = "EC",
        ["crv"] = "P-256",
        ["x"] = SpecVectors.IssuerKeyX,
        ["y"] = SpecVectors.IssuerKeyY,
    });

    private string Issue()
    {
        var header = new JsonObject { ["typ"] = SdJwtVc.Type, ["kid"] = "demo-1" };
        var plain = new JsonObject
        {
            ["iss"] = "http://localhost:5101",
            ["vct"] = "urn:demo-goc-kurumu:ikamet-izni:1",
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.AddYears(1).ToUnixTimeSeconds(),
            ["cnf"] = new JsonObject { ["jwk"] = EcJwk.FromPublicKey(_holderKey) },
        };
        var disclosable = new Dictionary<string, JsonNode?>
        {
            ["given_name"] = "Amina",
            ["family_name"] = "Haddad",
            ["birthdate"] = "1995-03-15",
            ["nationality"] = "SYR",
            ["permit_type"] = "ShortTerm",
        };
        return SdJwt.Issue(header, plain, disclosable, _issuerKey, decoyCount: 3);
    }
}
