using DijitalKimlik.Domain.Foreigners;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent API eşlemesi. Domain sınıflarında EF attribute'u yok; veritabanı ayrıntısı burada.
/// </summary>
internal sealed class ForeignerConfiguration : IEntityTypeConfiguration<Foreigner>
{
    public void Configure(EntityTypeBuilder<Foreigner> builder)
    {
        builder.ToTable("Foreigners");

        // Value object, value converter ile tek kolona yazılır: char(11), ASCII.
        builder.Property(f => f.IdentityNumber)
            .HasConversion(v => v.Value, v => ForeignerIdentityNumber.Create(v))
            .HasMaxLength(ForeignerIdentityNumber.Length)
            .IsFixedLength()
            .IsUnicode(false);

        // Mükerrer kaydın asıl güvencesi (uygulamadaki ExistsAsync kontrolü yarışa açık).
        builder.HasIndex(f => f.IdentityNumber).IsUnique();

        // İsimler Türkçe dışı karakterler içerebilir (Arapça, Kiril): nvarchar.
        builder.Property(f => f.GivenName).HasMaxLength(Foreigner.NameMaxLength);
        builder.Property(f => f.FamilyName).HasMaxLength(Foreigner.NameMaxLength);
        builder.Property(f => f.Nationality).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(f => f.PassportNumber).HasMaxLength(Foreigner.PassportMaxLength).IsUnicode(false);

        // Memur portalındaki aramalar için.
        builder.HasIndex(f => new { f.FamilyName, f.GivenName });
        builder.HasIndex(f => f.Nationality);

        builder.HasMany(f => f.Permits)
            .WithOne(p => p.Foreigner)
            .HasForeignKey(p => p.ForeignerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Permits salt okunur; EF koleksiyona _permits alanı üzerinden yazar.
        builder.Navigation(f => f.Permits).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
