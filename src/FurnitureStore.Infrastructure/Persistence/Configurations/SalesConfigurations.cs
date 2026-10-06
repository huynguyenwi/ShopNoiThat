using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.Property(c => c.UserId).HasMaxLength(450);
        builder.Property(c => c.AnonymousId).HasMaxLength(64);
        builder.Property(c => c.CouponCode).HasMaxLength(50);
        builder.ConfigureAudit();

        // One cart per user and one per anonymous visitor.
        builder.HasIndex(c => c.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        builder.HasIndex(c => c.AnonymousId).IsUnique().HasFilter("[AnonymousId] IS NOT NULL");

        builder.HasOne<ApplicationUser>()
            .WithOne(u => u.Cart)
            .HasForeignKey<Cart>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", table =>
        {
            table.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0");
        });

        builder.HasIndex(i => new { i.CartId, i.ProductVariantId }).IsUnique();
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);

        builder.HasOne(i => i.Cart).WithMany(c => c.Items).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Amounts",
                "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [ShippingFee] >= 0 AND [TotalAmount] >= 0");
        });

        builder.Property(o => o.OrderCode).HasMaxLength(30).IsRequired();
        builder.Property(o => o.UserId).HasMaxLength(450).IsRequired();
        builder.Property(o => o.CouponCode).HasMaxLength(50);
        builder.Property(o => o.CustomerName).HasMaxLength(150).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(20).IsRequired();
        builder.Property(o => o.CustomerEmail).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CustomerNote).HasMaxLength(1000);
        builder.Property(o => o.AdminNote).HasMaxLength(1000);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.Version).IsConcurrencyToken();
        builder.ConfigureAudit();

        builder.HasIndex(o => o.OrderCode).IsUnique();
        builder.HasIndex(o => new { o.UserId, o.PlacedAt });
        builder.HasIndex(o => new { o.Status, o.PlacedAt });
        builder.HasIndex(o => o.PlacedAt);

        // Users are locked, never deleted, while they have orders.
        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Coupon)
            .WithMany()
            .HasForeignKey(o => o.CouponId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_OrderItems_Amounts", "[UnitPrice] >= 0 AND [LineTotal] >= 0");
        });

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.VariantName).HasMaxLength(200);
        builder.Property(i => i.Sku).HasMaxLength(60).IsRequired();
        builder.Property(i => i.ImageUrl).HasMaxLength(500);
        builder.Property(i => i.ColorName).HasMaxLength(100);
        builder.Property(i => i.MaterialName).HasMaxLength(100);
        builder.Property(i => i.SizeName).HasMaxLength(100);

        builder.HasIndex(i => i.ProductId);
        builder.HasIndex(i => i.ProductVariantId);

        builder.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        // Product and variant are kept as optional links; the snapshot columns keep the order readable.
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class OrderAddressConfiguration : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("OrderAddresses");
        builder.Property(a => a.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256);
        builder.Property(a => a.AddressLine).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Ward).HasMaxLength(100).IsRequired();
        builder.Property(a => a.District).HasMaxLength(100);
        builder.Property(a => a.Province).HasMaxLength(100).IsRequired();

        builder.HasIndex(a => new { a.OrderId, a.AddressType }).IsUnique();
        builder.HasOne(a => a.Order).WithMany(o => o.Addresses).HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistories");
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.Property(h => h.ChangedBy).HasMaxLength(256);
        builder.HasIndex(h => new { h.OrderId, h.ChangedAt });
        builder.HasOne(h => h.Order).WithMany(o => o.StatusHistory).HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
        });
        builder.Property(p => p.Provider).HasMaxLength(50);
        builder.Property(p => p.TransactionCode).HasMaxLength(100);
        builder.Property(p => p.ProviderResponseCode).HasMaxLength(50);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.ConfigureAudit();

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.TransactionCode);
        builder.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons", table =>
        {
            table.HasCheckConstraint("CK_Coupons_DiscountValue", "[DiscountValue] >= 0");
            table.HasCheckConstraint("CK_Coupons_Dates", "[StartsAt] IS NULL OR [EndsAt] IS NULL OR [StartsAt] <= [EndsAt]");
        });
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Version).IsConcurrencyToken();
        builder.ConfigureAudit();

        builder.HasIndex(c => c.Code).IsUnique();
    }
}

internal sealed class CouponUsageConfiguration : IEntityTypeConfiguration<CouponUsage>
{
    public void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        builder.ToTable("CouponUsages");
        builder.Property(u => u.UserId).HasMaxLength(450).IsRequired();

        builder.HasIndex(u => new { u.CouponId, u.UserId });
        builder.HasIndex(u => u.OrderId).IsUnique();

        builder.HasOne(u => u.Coupon).WithMany(c => c.Usages).HasForeignKey(u => u.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.Order).WithMany().HasForeignKey(u => u.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
