using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");
        builder.Property(c => c.CustomerId).HasMaxLength(450);
        builder.Property(c => c.GuestKey).HasMaxLength(64);
        builder.Property(c => c.CustomerName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.CustomerEmail).HasMaxLength(256);
        builder.Property(c => c.CustomerPhone).HasMaxLength(20);
        builder.Property(c => c.AssignedStaffId).HasMaxLength(450);
        builder.Property(c => c.Subject).HasMaxLength(200);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(200);
        builder.ConfigureAudit();

        builder.HasIndex(c => c.CustomerId);
        builder.HasIndex(c => c.GuestKey);
        builder.HasIndex(c => new { c.Status, c.LastMessageAt });

        builder.HasOne<ApplicationUser>().WithMany(u => u.ChatConversations).HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.AssignedStaffId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.Property(m => m.SenderId).HasMaxLength(450);
        builder.Property(m => m.SenderName).HasMaxLength(150).IsRequired();
        builder.Property(m => m.Content).HasMaxLength(ChatMessage.MaxContentLength).IsRequired();

        builder.HasIndex(m => new { m.ConversationId, m.SentAt });
        builder.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        builder.ToTable("ContactMessages");
        builder.Property(m => m.FullName).HasMaxLength(150).IsRequired();
        builder.Property(m => m.Phone).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Email).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(200);
        builder.Property(m => m.Message).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.UserId).HasMaxLength(450);
        builder.Property(m => m.IpAddress).HasMaxLength(45);
        builder.Property(m => m.AdminNote).HasMaxLength(1000);
        builder.ConfigureAudit();

        builder.HasIndex(m => new { m.Status, m.CreatedAt });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.Property(n => n.UserId).HasMaxLength(450);
        builder.Property(n => n.RecipientRole).HasMaxLength(50);
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(500);

        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
        builder.HasIndex(n => new { n.RecipientRole, n.IsRead, n.CreatedAt });
        builder.HasOne<ApplicationUser>().WithMany(u => u.Notifications).HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
