using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>
/// Kimlik bilgilerini, erişim token'larını ve durum listesini imzalayan P-256 anahtarı.
/// Geliştirmede bir PEM dosyasında durur ve ilk açılışta üretilir. Üretimde anahtar HSM'de ya da
/// Azure Key Vault / AWS KMS'te tutulur; uygulama yalnız imzalatır, özel anahtarı hiç görmez.
/// </summary>
public sealed class IssuerSigningKey : IDisposable
{
    // ECDsa nesnesinin örnek metotlarının eşzamanlı kullanımı belgede güvenli diye geçmiyor.
    private readonly Lock _gate = new();
    private readonly ECDsa _key;
    private readonly JsonObject _publicJwk;

    public IssuerSigningKey(IOptions<IssuerOptions> options, IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, options.Value.SigningKeyPath);
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(path))
        {
            _key.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _key.ExportECPrivateKeyPem());
        }

        _publicJwk = EcJwk.FromPublicKey(_key);
        KeyId = EcJwk.Thumbprint(_publicJwk);
        _publicJwk["kid"] = KeyId;
        _publicJwk["use"] = "sig";
        _publicJwk["alg"] = Jws.Es256;
    }

    /// <summary>Anahtar kimliği (<c>kid</c>): JWK parmak izi. Doğrulayıcı JWKS'te anahtarı bununla bulur.</summary>
    public string KeyId { get; }

    public JsonObject PublicJwk => (JsonObject)_publicJwk.DeepClone();

    public T Use<T>(Func<ECDsa, T> operation)
    {
        lock (_gate)
        {
            return operation(_key);
        }
    }

    public string Sign(JsonObject header, JsonObject payload) => Use(key => Jws.Sign(header, payload, key));

    public void Dispose() => _key.Dispose();
}
