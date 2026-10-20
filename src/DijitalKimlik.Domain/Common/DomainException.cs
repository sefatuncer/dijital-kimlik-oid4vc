namespace DijitalKimlik.Domain.Common;

/// <summary>
/// Bir iş kuralı (invariant) ihlal edildiğinde fırlatılır. Kod, makinece okunacak sabit bir
/// değerdir (ör. "credential.invalid_transition"); mesaj insan içindir.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
