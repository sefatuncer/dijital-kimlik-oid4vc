using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>
/// OpenID4VCI §7: <c>c_nonce</c> üretir ve kanıt JWT'sinde yalnız bir kez kabul eder. Tek kullanım,
/// yakalanan bir kanıtın yeniden gönderilmesini (replay) engeller. Bellekte durur: tek sunuculu
/// kurulumda yeterli; birden çok sunucuda Redis gibi paylaşılan bir depoya taşınır.
/// </summary>
public sealed class NonceStore(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);

    public string Issue()
    {
        var now = clock.GetUtcNow();
        foreach (var (nonce, expiresAt) in _nonces)
        {
            if (expiresAt <= now)
            {
                _nonces.TryRemove(nonce, out _);
            }
        }

        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        _nonces[value] = now + Lifetime;
        return value;
    }

    /// <summary><c>TryRemove</c> atomiktir: aynı nonce eşzamanlı iki istekte de yalnız birinde geçer.</summary>
    public bool TryConsume(string nonce) =>
        _nonces.TryRemove(nonce, out var expiresAt) && expiresAt > clock.GetUtcNow();
}
