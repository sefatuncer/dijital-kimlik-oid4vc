using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.StatusLists;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

internal sealed class IssuedCredentialConfiguration : IEntityTypeConfiguration<IssuedCredential>
{
    public void Configure(EntityTypeBuilder<IssuedCredential> builder)
    {
        // Tabloda trigger var (denetim kaydı). EF Core varsayılan olarak UPDATE ... OUTPUT INSERTED.RowVersion
        // kullanır; SQL Server, trigger'lı tabloda INTO'suz OUTPUT'a izin vermez. HasTrigger ile EF
        // daha yavaş ama uyumlu yola geçer: yeni değerleri ayrı bir SELECT ile okur.
        builder.ToTable("IssuedCredentials", t => t.HasTrigger("trg_IssuedCredentials_StatusAudit"));

        builder.Property(c => c.CredentialId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(c => c.CredentialId).IsUnique();

        builder.Property(c => c.Format).HasMaxLength(32).IsUnicode(false);
        builder.Property(c => c.Vct).HasMaxLength(200).IsUnicode(false);
        builder.Property(c => c.HolderKeyThumbprint).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(c => c.HolderKeyThumbprint);

        builder.Property(c => c.Status).HasConversion<byte>();
        builder.Property(c => c.StatusReason).HasMaxLength(500);

        // SQL Server rowversion: her UPDATE'te otomatik artar; EF bunu WHERE koşuluna ekler.
        builder.Property(c => c.RowVersion).IsRowVersion();

        // Hesaplanan özellik, kolon değil.
        builder.Ignore(c => c.StatusListValue);

        // Bir status list sırası yalnız bir kimlik bilgisine ait olabilir.
        builder.HasIndex(c => new { c.StatusListId, c.StatusListIndex }).IsUnique();

        // Windows servisi süresi dolan aktif/askıdaki kayıtları bu indeksle bulur.
        builder.HasIndex(c => c.ExpiresAt).HasFilter("[Status] IN (1, 2)");

        builder.HasOne(c => c.ResidencePermit)
            .WithMany()
            .HasForeignKey(c => c.ResidencePermitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StatusList>()
            .WithMany()
            .HasForeignKey(c => c.StatusListId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
