using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.Property(a => a.UserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.Label).HasMaxLength(50);
        builder.Property(a => a.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(20).IsRequired();
        builder.Property(a => a.AddressLine).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Ward).HasMaxLength(100).IsRequired();
        builder.Property(a => a.District).HasMaxLength(100);
        builder.Property(a => a.Province).HasMaxLength(100).IsRequired();
        builder.ConfigureAudit();

        builder.HasIndex(a => a.UserId);
        builder.HasOne<ApplicationUser>().WithMany(u => u.Addresses).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("Wishlists");
        builder.Property(w => w.UserId).HasMaxLength(450).IsRequired();
        builder.ConfigureAudit();

        builder.HasIndex(w => w.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithOne(u => u.Wishlist).HasForeignKey<Wishlist>(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("WishlistItems");
        builder.HasIndex(i => new { i.WishlistId, i.ProductId }).IsUnique();
        builder.HasQueryFilter(i => !i.Product.IsDeleted);

        builder.HasOne(i => i.Wishlist).WithMany(w => w.Items).HasForeignKey(i => i.WishlistId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews", table =>
        {
            table.HasCheckConstraint("CK_Reviews_Rating", $"[Rating] >= {Review.MinRating} AND [Rating] <= {Review.MaxRating}");
        });
        builder.Property(r => r.UserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.ReviewerName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(200);
        builder.Property(r => r.Comment).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.AdminReply).HasMaxLength(2000);
        builder.ConfigureAudit();

        // One review per customer per product.
        builder.HasIndex(r => new { r.UserId, r.ProductId }).IsUnique();
        builder.HasIndex(r => new { r.ProductId, r.IsHidden, r.CreatedAt });
        builder.HasQueryFilter(r => !r.Product.IsDeleted);

        builder.HasOne(r => r.Product).WithMany(p => p.Reviews).HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.OrderItem).WithMany().HasForeignKey(r => r.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany(u => u.Reviews).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReviewImageConfiguration : IEntityTypeConfiguration<ReviewImage>
{
    public void Configure(EntityTypeBuilder<ReviewImage> builder)
    {
        builder.ToTable("ReviewImages");
        builder.Property(i => i.Url).HasMaxLength(500).IsRequired();
        builder.HasIndex(i => i.ReviewId);
        builder.HasQueryFilter(i => !i.Review.Product.IsDeleted);
        builder.HasOne(i => i.Review).WithMany(r => r.Images).HasForeignKey(i => i.ReviewId).OnDelete(DeleteBehavior.Cascade);
    }
}
