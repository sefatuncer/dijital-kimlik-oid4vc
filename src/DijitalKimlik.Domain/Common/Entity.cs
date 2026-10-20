namespace DijitalKimlik.Domain.Common;

/// <summary>
/// Kimliği (Id) olan tüm varlıkların tabanı. Id veritabanında IDENTITY ile üretilir.
/// </summary>
/// <remarks>
/// Rastgele GUID yerine int kullanıyoruz: clustered index sıralı büyür, sayfa bölünmesi
/// (page split) olmaz. Dışarıya açılan kimliklerde (URL, token) tahmin edilemez değerler
/// ayrıca tutulur; iç Id hiçbir zaman dışarı verilmez.
/// </remarks>
public abstract class Entity
{
    public int Id { get; protected set; }
}
