using DijitalKimlik.Domain.Auditing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditLog", IssuerDbContext.AuditSchema);

        builder.Property(a => a.TableName).HasMaxLength(128);
        builder.Property(a => a.RecordKey).HasMaxLength(64);
        builder.Property(a => a.Action).HasMaxLength(16);
        builder.Property(a => a.ChangedBy).HasMaxLength(128);
        builder.Property(a => a.ChangedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.HasIndex(a => new { a.TableName, a.RecordKey });
    }
}
