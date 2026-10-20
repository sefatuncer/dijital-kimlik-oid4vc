namespace DijitalKimlik.Application.Abstractions;

/// <summary>
/// Veritabanındaki bir unique kısıtı ihlal edildi. Infrastructure, sağlayıcıya özgü hatayı
/// (SQL Server 2601/2627) bu türe çevirir; Application katmanı SQL Server'ı tanımak zorunda kalmaz.
/// </summary>
public sealed class UniqueConstraintException : Exception
{
    public UniqueConstraintException()
    {
    }

    public UniqueConstraintException(string message)
        : base(message)
    {
    }

    public UniqueConstraintException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
