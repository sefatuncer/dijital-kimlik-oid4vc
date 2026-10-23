using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using DijitalKimlik.Crypto.Jose;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Verifier.Presentations;

/// <summary>
/// Bir doğrulama oturumu. <c>nonce</c> sunumu bu isteğe bağlar (replay koruması); <c>state</c> yanıtı
/// oturumla eşleştirir; <c>response_code</c> sonuç sayfasını yalnız cüzdanın yönlendirdiği tarayıcıya açar.
/// </summary>
public sealed class Transaction
{
    private VerificationResult? _result;

    public required string Id { get; init; }

    public required Scenario Scenario { get; init; }

    public required string Nonce { get; init; }

    public required string State { get; init; }

    public required string ResponseCode { get; init; }

    /// <summary>Oturumu başlatan tarayıcının çerezi: QR'ı gösteren sayfa sonucu bununla alır.</summary>
    public required string SessionId { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public VerificationResult? Result => Volatile.Read(ref _result);

    /// <summary>Yanıt yalnız bir kez kabul edilir: aynı state ile ikinci gönderim reddedilir.</summary>
    public bool TryComplete(VerificationResult result) => Interlocked.CompareExchange(ref _result, result, null) is null;

    public bool OwnedBy(string? sessionId) => sessionId is not null && FixedTimeEquals(sessionId, SessionId);

    public bool AcceptsCode(string? code) => code is not null && FixedTimeEquals(code, ResponseCode);

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}

/// <summary>Oturumlar bellekte durur. Birden çok sunucuda Redis ya da veritabanına taşınır.</summary>
public sealed class TransactionStore(IOptions<VerifierOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Transaction> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Transaction> _byState = new(StringComparer.Ordinal);

    public Transaction Create(Scenario scenario, string sessionId)
    {
        RemoveExpired();
        var transaction = new Transaction
        {
            Id = Random(16),
            Scenario = scenario,
            Nonce = Random(16),
            State = Random(16),
            ResponseCode = Random(16),
            SessionId = sessionId,
            ExpiresAt = clock.GetUtcNow() + options.Value.TransactionLifetime,
        };
        _byId[transaction.Id] = transaction;
        _byState[transaction.State] = transaction;
        return transaction;
    }

    public Transaction? Find(string id) =>
        _byId.TryGetValue(id, out var t) && t.ExpiresAt > clock.GetUtcNow() ? t : null;

    public Transaction? FindByState(string state) =>
        _byState.TryGetValue(state, out var t) && t.ExpiresAt > clock.GetUtcNow() ? t : null;

    /// <summary>Cüzdana verilen yetkilendirme isteğinin parametreleri (by value, imzasız).</summary>
    public string AuthorizationQuery(Transaction transaction) =>
        QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = options.Value.ClientId,
            ["response_type"] = "vp_token",
            ["response_mode"] = "direct_post",
            ["response_uri"] = options.Value.ResponseUri,
            ["nonce"] = transaction.Nonce,
            ["state"] = transaction.State,
            ["dcql_query"] = JoseJson.Serialize(transaction.Scenario.DcqlQuery()),
        }).Value!.TrimStart('?');

    public static string Random(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (id, transaction) in _byId)
        {
            if (transaction.ExpiresAt <= now)
            {
                _byId.TryRemove(id, out _);
                _byState.TryRemove(transaction.State, out _);
            }
        }
    }
}
