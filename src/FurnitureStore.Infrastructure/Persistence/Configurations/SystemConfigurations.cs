using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(45);

        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
        builder.HasIndex(a => a.UserId);
    }
}

internal sealed class StoreInfoConfiguration : IEntityTypeConfiguration<StoreInfo>
{
    public void Configure(EntityTypeBuilder<StoreInfo> builder)
    {
        builder.ToTable("StoreInformation");
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LogoSubtitle).HasMaxLength(40);
        builder.Property(s => s.LogoUrl).HasMaxLength(500);
        builder.Property(s => s.Tagline).HasMaxLength(200);
        builder.Property(s => s.About).HasMaxLength(4000);
        builder.Property(s => s.Address).HasMaxLength(300).IsRequired();
        builder.Property(s => s.WorkshopAddress).HasMaxLength(300);
        builder.Property(s => s.Hotline).HasMaxLength(30).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(256).IsRequired();
        builder.Property(s => s.OpeningHours).HasMaxLength(150);
        builder.Property(s => s.FacebookUrl).HasMaxLength(300);
        builder.Property(s => s.TikTokUrl).HasMaxLength(300);
        builder.Property(s => s.ZaloUrl).HasMaxLength(300);
        builder.Property(s => s.GoogleMapsEmbedUrl).HasMaxLength(1000);
        builder.ConfigureAudit();
    }
}

internal sealed class HomeBannerConfiguration : IEntityTypeConfiguration<HomeBanner>
{
    public void Configure(EntityTypeBuilder<HomeBanner> builder)
    {
        builder.ToTable("HomeBanners");
        builder.Property(b => b.Eyebrow).HasMaxLength(60);
        builder.Property(b => b.Title).HasMaxLength(120).IsRequired();
        builder.Property(b => b.TitleHighlight).HasMaxLength(80);
        builder.Property(b => b.Description).HasMaxLength(400);
        builder.Property(b => b.PrimaryButtonText).HasMaxLength(40);
        builder.Property(b => b.PrimaryButtonUrl).HasMaxLength(300);
        builder.Property(b => b.SecondaryButtonText).HasMaxLength(40);
        builder.Property(b => b.SecondaryButtonUrl).HasMaxLength(300);
        builder.Property(b => b.Stat1Value).HasMaxLength(20);
        builder.Property(b => b.Stat1Label).HasMaxLength(40);
        builder.Property(b => b.Stat2Value).HasMaxLength(20);
        builder.Property(b => b.Stat2Label).HasMaxLength(40);
        builder.Property(b => b.Stat3Value).HasMaxLength(20);
        builder.Property(b => b.Stat3Label).HasMaxLength(40);
        builder.Property(b => b.ImageUrl).HasMaxLength(500);
        builder.Property(b => b.ImageAlt).HasMaxLength(200);
        builder.ConfigureAudit();
    }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.Property(u => u.Gender).HasMaxLength(20);
        builder.HasIndex(u => u.CreatedAt);
    }
}

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(r => r.Description).HasMaxLength(256);
    }
}
