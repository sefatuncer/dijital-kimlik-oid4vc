namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>
/// İstemciye <c>{"error": ..., "error_description": ...}</c> olarak dönen protokol hatası
/// (OpenID4VCI §8.3.1.2, RFC 6749 §5.2). Açıklama kişisel veri içermez; günlüğe de böyle yazılır.
/// </summary>
public sealed class Oid4VciException(string error, string description) : Exception(description)
{
    public string Error { get; } = error;
}
