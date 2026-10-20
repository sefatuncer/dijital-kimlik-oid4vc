using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.Domain.Credentials;

public enum CredentialStatus
{
    Active = 1,
    Suspended = 2,
    Revoked = 3,
    Expired = 4,
}

/// <summary>
/// Cüzdana verilmiş bir kimlik bilgisinin kurum tarafındaki kaydı.
/// </summary>
/// <remarks>
/// Veri minimizasyonu (KVKK): kimlik bilgisinin kendisi (SD-JWT) burada saklanmaz. Kurum yalnız
/// iptal ve denetim için gereken üst veriyi tutar: hangi izne ait olduğu, holder anahtarının
/// parmak izi, status list'teki yeri ve durumu.
/// </remarks>
public sealed class IssuedCredential : Entity
{
    public const string SdJwtVcFormat = "dc+sd-jwt";

    // Durum makinesi: hangi durumdan hangisine geçilebilir. Revoked ve Expired son durumlardır.
    private static readonly Dictionary<CredentialStatus, CredentialStatus[]> AllowedTransitions = new()
    {
        [CredentialStatus.Active] = [CredentialStatus.Suspended, CredentialStatus.Revoked, CredentialStatus.Expired],
        [CredentialStatus.Suspended] = [CredentialStatus.Active, CredentialStatus.Revoked, CredentialStatus.Expired],
        [CredentialStatus.Revoked] = [],
        [CredentialStatus.Expired] = [],
    };

    private IssuedCredential()
    {
    }

    public IssuedCredential(
        string credentialId,
        ResidencePermit permit,
        string vct,
        string holderKeyThumbprint,
        int statusListId,
        int statusListIndex,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (expiresAt <= issuedAt)
        {
            throw new DomainException("credential.invalid_validity", "Bitiş zamanı veriliş zamanından sonra olmalı.");
        }

        CredentialId = credentialId;
        ResidencePermit = permit;
        Format = SdJwtVcFormat;
        Vct = vct;
        HolderKeyThumbprint = holderKeyThumbprint;
        StatusListId = statusListId;
        StatusListIndex = statusListIndex;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        Status = CredentialStatus.Active;
    }

    /// <summary>Dışarıya açılan, tahmin edilemez kimlik (ör. "urn:uuid:...").</summary>
    public string CredentialId { get; private set; } = string.Empty;

    public int ResidencePermitId { get; private set; }

    public ResidencePermit ResidencePermit { get; private set; } = null!;

    public string Format { get; private set; } = string.Empty;

    /// <summary>Verifiable Credential Type, kimlik bilgisinin türünü belirten URI.</summary>
    public string Vct { get; private set; } = string.Empty;

    /// <summary>Holder açık anahtarının JWK thumbprint'i (RFC 7638). Kimlik bilgisi bu anahtara bağlıdır.</summary>
    public string HolderKeyThumbprint { get; private set; } = string.Empty;

    public int StatusListId { get; private set; }

    public int StatusListIndex { get; private set; }

    public CredentialStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? StatusChangedAt { get; private set; }

    /// <summary>
    /// İyimser eşzamanlılık (optimistic concurrency) için SQL Server rowversion. İki memur aynı
    /// kaydı aynı anda değiştirirse ikincisi DbUpdateConcurrencyException alır.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Status list'e yazılacak değer: 0 geçerli, 1 geçersiz, 2 askıda.
    /// Süresi dolanlar da 1 yazılır; doğrulayıcı ayrıca exp alanına bakar.
    /// </summary>
    public byte StatusListValue => Status switch
    {
        CredentialStatus.Active => 0x00,
        CredentialStatus.Revoked or CredentialStatus.Expired => 0x01,
        CredentialStatus.Suspended => 0x02,
        _ => throw new InvalidOperationException($"Bilinmeyen durum: {Status}"),
    };

    public void Suspend(string reason, DateTimeOffset now) => TransitionTo(CredentialStatus.Suspended, now, reason);

    public void Reinstate(DateTimeOffset now) => TransitionTo(CredentialStatus.Active, now, reason: null);

    public void Revoke(string reason, DateTimeOffset now) => TransitionTo(CredentialStatus.Revoked, now, reason);

    /// <summary>Süresi dolmuşsa Expired durumuna geçirir. Windows servisi periyodik olarak çağırır.</summary>
    public bool TryExpire(DateTimeOffset now)
    {
        if (now < ExpiresAt || !CanTransitionTo(CredentialStatus.Expired))
        {
            return false;
        }

        TransitionTo(CredentialStatus.Expired, now, "Geçerlilik süresi doldu.");
        return true;
    }

    public bool CanTransitionTo(CredentialStatus target) => AllowedTransitions[Status].Contains(target);

    private void TransitionTo(CredentialStatus target, DateTimeOffset now, string? reason)
    {
        if (!CanTransitionTo(target))
        {
            throw new DomainException("credential.invalid_transition", $"{Status} durumundan {target} durumuna geçilemez.");
        }

        if (target is CredentialStatus.Suspended or CredentialStatus.Revoked && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("credential.reason_required", "Askıya alma ve iptal için gerekçe zorunlu.");
        }

        Status = target;
        StatusReason = reason?.Trim();
        StatusChangedAt = now;
    }
}
