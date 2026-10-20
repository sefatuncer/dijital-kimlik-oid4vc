using DijitalKimlik.Domain.Permits;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

internal sealed class ResidencePermitConfiguration : IEntityTypeConfiguration<ResidencePermit>
{
    public void Configure(EntityTypeBuilder<ResidencePermit> builder)
    {
        // Domain kuralı veritabanında da korunur: uygulama dışından yapılan bir INSERT de reddedilir.
        builder.ToTable("ResidencePermits", t =>
            t.HasCheckConstraint("CK_ResidencePermits_Period", "[ValidUntil] >= [ValidFrom]"));

        builder.Property(p => p.PermitNumber).HasMaxLength(ResidencePermit.PermitNumberMaxLength).IsUnicode(false);
        builder.HasIndex(p => p.PermitNumber).IsUnique();

        // Enum'lar tinyint olarak saklanır (int'e göre 3 bayt tasarruf, 20.000 satırda önemsiz ama doğru alışkanlık).
        builder.Property(p => p.Type).HasConversion<byte>();
        builder.Property(p => p.Status).HasConversion<byte>();
        builder.Property(p => p.CancellationReason).HasMaxLength(500);

        builder.HasIndex(p => new { p.ForeignerId, p.Status });
    }
}
