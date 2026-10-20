using DijitalKimlik.Domain.Offers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

internal sealed class CredentialOfferConfiguration : IEntityTypeConfiguration<CredentialOffer>
{
    // base64url(SHA-256) her zaman 43 karakterdir.
    private const int HashLength = 43;

    public void Configure(EntityTypeBuilder<CredentialOffer> builder)
    {
        builder.ToTable("CredentialOffers");

        builder.HasIndex(o => o.PublicId).IsUnique();

        // Token uç noktası teklifi kod özetiyle arar: bu indeks olmadan her istekte tablo taranır.
        builder.Property(o => o.PreAuthorizedCodeHash).HasMaxLength(HashLength).IsFixedLength().IsUnicode(false);
        builder.HasIndex(o => o.PreAuthorizedCodeHash).IsUnique();

        builder.Property(o => o.TxCodeHash).HasMaxLength(HashLength).IsFixedLength().IsUnicode(false);
        builder.Property(o => o.Status).HasConversion<byte>();
        builder.Property(o => o.CreatedBy).HasMaxLength(100);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasOne(o => o.ResidencePermit)
            .WithMany()
            .HasForeignKey(o => o.ResidencePermitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Filtreli indeks: yalnız bekleyen teklifler. Temizlik işi süresi geçenleri hızlı bulur.
        builder.HasIndex(o => o.ExpiresAt).HasFilter("[Status] = 1");
    }
}
