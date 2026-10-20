using DijitalKimlik.Domain.Common;

namespace DijitalKimlik.Domain.StatusLists;

/// <summary>
/// İptal ve askı bilgisinin yayımlandığı liste (Token Status List). Her kimlik bilgisi listede
/// bir sıra numarasına (index) sahiptir; o sıradaki bitler durumunu söyler.
/// </summary>
/// <remarks>
/// Asıl durum <see cref="Credentials.IssuedCredential.Status"/> alanındadır. Liste ondan türetilir
/// ve Windows servisi tarafından imzalanıp <see cref="PublishedToken"/> olarak yayımlanır.
/// Kapasite gerçek kullanıcı sayısından büyük tutulur ve sıra numaraları rastgele dağıtılır.
/// Böylece bir sıra numarasından kişinin kayıt sırası tahmin edilemez.
/// </remarks>
public sealed class StatusList : Entity
{
    /// <summary>Kimlik bilgisi başına bit sayısı. 2 bit: geçerli, geçersiz, askıda.</summary>
    public const int BitsPerEntry = 2;

    private StatusList()
    {
    }

    public StatusList(int capacity, DateTimeOffset createdAt)
    {
        if (capacity <= 0 || capacity % 8 != 0)
        {
            throw new DomainException("status_list.invalid_capacity", "Kapasite 8'in pozitif katı olmalı.");
        }

        Capacity = capacity;
        CreatedAt = createdAt;
    }

    public int Capacity { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Son yayımlanan imzalı liste (statuslist+jwt).</summary>
    public string? PublishedToken { get; private set; }

    public void Publish(string signedToken, DateTimeOffset now)
    {
        PublishedToken = signedToken;
        PublishedAt = now;
        Version++;
    }
}
