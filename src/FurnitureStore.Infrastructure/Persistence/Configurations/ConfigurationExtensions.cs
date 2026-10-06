using FurnitureStore.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Column sizes for the audit columns shared by every <see cref="AuditableEntity"/>.</summary>
    public static EntityTypeBuilder<T> ConfigureAudit<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedBy).HasMaxLength(256);
        builder.Property(e => e.UpdatedBy).HasMaxLength(256);
        return builder;
    }
}
