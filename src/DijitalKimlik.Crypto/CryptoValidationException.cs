namespace DijitalKimlik.Crypto;

/// <summary>
/// İmza, biçim ya da kural doğrulanamadı. <see cref="Code"/> hatanın makine okunur adıdır
/// (ör. <c>invalid_signature</c>, <c>invalid_nonce</c>); arayüz ve günlük bu adı kullanır.
/// </summary>
public sealed class CryptoValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
