using DijitalKimlik.Domain.StatusLists;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DijitalKimlik.Infrastructure.Persistence.Configurations;

internal sealed class StatusListConfiguration : IEntityTypeConfiguration<StatusList>
{
    public void Configure(EntityTypeBuilder<StatusList> builder)
    {
        builder.ToTable("StatusLists");

        // İmzalı JWT yalnız ASCII karakter içerir.
        builder.Property(s => s.PublishedToken).IsUnicode(false);

        // İki yayıncı aynı anda yayımlarsa ikincisi eşzamanlılık hatası alır.
        builder.Property(s => s.Version).IsConcurrencyToken();
    }
}
