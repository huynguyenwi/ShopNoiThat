using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class AIConversationConfiguration : IEntityTypeConfiguration<AIConversation>
{
    public void Configure(EntityTypeBuilder<AIConversation> builder)
    {
        builder.ToTable("AIConversations");
        builder.Property(c => c.UserId).HasMaxLength(450);
        builder.Property(c => c.AnonymousId).HasMaxLength(64);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.ConfigureAudit();

        builder.HasIndex(c => new { c.UserId, c.LastMessageAt });
        builder.HasIndex(c => c.AnonymousId);

        builder.HasOne<ApplicationUser>().WithMany(u => u.AIConversations).HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AIMessageConfiguration : IEntityTypeConfiguration<AIMessage>
{
    public void Configure(EntityTypeBuilder<AIMessage> builder)
    {
        builder.ToTable("AIMessages");
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.Model).HasMaxLength(100);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        builder.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AIKnowledgeEntryConfiguration : IEntityTypeConfiguration<AIKnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<AIKnowledgeEntry> builder)
    {
        builder.ToTable("AIKnowledgeEntries");
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Content).HasMaxLength(4000).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Keywords).HasMaxLength(500);
        builder.ConfigureAudit();

        builder.HasIndex(e => new { e.IsActive, e.Category, e.DisplayOrder });
    }
}

internal sealed class QuoteRequestConfiguration : IEntityTypeConfiguration<QuoteRequest>
{
    public void Configure(EntityTypeBuilder<QuoteRequest> builder)
    {
        builder.ToTable("QuoteRequests", table =>
        {
            table.HasCheckConstraint("CK_QuoteRequests_Dimensions", "[LengthMm] > 0 AND [WidthMm] > 0 AND [HeightMm] > 0");
            table.HasCheckConstraint("CK_QuoteRequests_Quantity", "[Quantity] > 0");
        });
        builder.Property(q => q.QuoteCode).HasMaxLength(30).IsRequired();
        builder.Property(q => q.UserId).HasMaxLength(450);
        builder.Property(q => q.CustomerName).HasMaxLength(150).IsRequired();
        builder.Property(q => q.Phone).HasMaxLength(20).IsRequired();
        builder.Property(q => q.Email).HasMaxLength(256);
        builder.Property(q => q.RawRequest).HasMaxLength(2000).IsRequired();
        builder.Property(q => q.ProductTypeName).HasMaxLength(100).IsRequired();
        builder.Property(q => q.MaterialName).HasMaxLength(100);
        builder.Property(q => q.ColorName).HasMaxLength(100);
        builder.Property(q => q.AiExplanation).HasMaxLength(4000);
        builder.Property(q => q.CustomerNote).HasMaxLength(1000);
        builder.Property(q => q.AdminNote).HasMaxLength(1000);
        builder.Property(q => q.QuotedBy).HasMaxLength(256);
        builder.Property(q => q.Version).IsConcurrencyToken();
        builder.ConfigureAudit();

        builder.HasIndex(q => q.QuoteCode).IsUnique();
        builder.HasIndex(q => new { q.Status, q.CreatedAt });
        builder.HasIndex(q => q.UserId);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Material).WithMany().HasForeignKey(q => q.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Style).WithMany().HasForeignKey(q => q.StyleId).OnDelete(DeleteBehavior.Restrict);
    }
}
