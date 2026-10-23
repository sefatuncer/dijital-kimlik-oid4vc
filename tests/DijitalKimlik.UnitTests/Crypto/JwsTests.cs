using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.UnitTests.Crypto;

public class JwsTests
{
    [Fact]
    public void Oid4vci_proof_vector_verifies_with_its_header_jwk()
    {
        var jws = Jws.Parse(SpecVectors.Oid4VciProofJwt);
        using var key = EcJwk.ToPublicKey((JsonObject)jws.Header["jwk"]!);

        jws.VerifySignature(key);

        Assert.Equal("openid4vci-proof+jwt", jws.Type);
        Assert.Equal("https://credential-issuer.example.com", JoseJson.GetString(jws.Payload, "aud"));
        Assert.Equal("LarRGSbmUPYtRYO6BQ4yn8", JoseJson.GetString(jws.Payload, "nonce"));
        Assert.Equal(1701960444L, JoseJson.GetInt64(jws.Payload, "iat"));
    }

    [Fact]
    public void Changed_payload_breaks_the_signature()
    {
        var parts = SpecVectors.Oid4VciProofJwt.Split('.');
        var forgedPayload = JoseJson.EncodeSegment(new JsonObject { ["aud"] = "https://attacker.example", ["iat"] = 1701960444 });
        var forged = Jws.Parse(parts[0] + "." + forgedPayload + "." + parts[2]);
        using var key = EcJwk.ToPublicKey((JsonObject)forged.Header["jwk"]!);

        var ex = Assert.Throws<CryptoValidationException>(() => forged.VerifySignature(key));
        Assert.Equal("invalid_signature", ex.Code);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    public void Only_es256_is_accepted(string algorithm)
    {
        var header = JoseJson.EncodeSegment(new JsonObject { ["alg"] = algorithm });
        var payload = JoseJson.EncodeSegment(new JsonObject { ["sub"] = "x" });
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var ex = Assert.Throws<CryptoValidationException>(() => Jws.Parse(header + "." + payload + ".").VerifySignature(key));
        Assert.Equal("unsupported_alg", ex.Code);
    }

    [Fact]
    public void Signed_token_verifies_and_alg_comes_first()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var compact = Jws.Sign(new JsonObject { ["typ"] = "kb+jwt" }, new JsonObject { ["nonce"] = "n" }, key);
        var jws = Jws.Parse(compact);
        jws.VerifySignature(key);

        string[] expected = ["alg", "typ"];
        Assert.Equal(expected, jws.Header.Select(p => p.Key));
    }

    [Fact]
    public void Jwk_with_private_key_is_rejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = EcJwk.FromPublicKey(key);
        jwk["d"] = "c2VjcmV0";

        var ex = Assert.Throws<CryptoValidationException>(() => EcJwk.ToPublicKey(jwk));
        Assert.Equal("private_key_in_jwk", ex.Code);
    }

    [Fact]
    public void Thumbprint_depends_on_the_key_not_on_member_order()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = EcJwk.FromPublicKey(key);
        var reordered = new JsonObject { ["y"] = jwk["y"]!.DeepClone(), ["x"] = jwk["x"]!.DeepClone(), ["crv"] = "P-256", ["kty"] = "EC" };

        Assert.Equal(EcJwk.Thumbprint(jwk), EcJwk.Thumbprint(reordered));
        Assert.NotEqual(EcJwk.Thumbprint(jwk), EcJwk.Thumbprint(EcJwk.FromPublicKey(other)));
        Assert.Equal(43, EcJwk.Thumbprint(jwk).Length);
    }

    [Fact]
    public void Serialized_json_keeps_plus_sign_and_turkish_letters_readable()
    {
        var json = JoseJson.Serialize(new JsonObject { ["typ"] = "dc+sd-jwt", ["name"] = "İkamet İzni" });

        Assert.Equal("""{"typ":"dc+sd-jwt","name":"İkamet İzni"}""", json);
    }
}
