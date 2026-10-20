namespace DijitalKimlik.Domain.Auditing;

/// <summary>
/// Denetim kaydı. Uygulama bu tabloya yazmaz; kayıtları veritabanındaki trigger üretir.
/// Uygulama katmanındaki bir hata ya da doğrudan SQL ile yapılan değişiklik de iz bırakır.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; private set; }

    public string TableName { get; private set; } = string.Empty;

    public string RecordKey { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public string ChangedBy { get; private set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; private set; }
}
