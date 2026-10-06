using FurnitureStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FurnitureStore.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(170).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.ImageUrl).HasMaxLength(500);
        builder.Property(c => c.IconCssClass).HasMaxLength(60);
        builder.Property(c => c.MetaTitle).HasMaxLength(200);
        builder.Property(c => c.MetaDescription).HasMaxLength(500);
        builder.ConfigureAudit();

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => new { c.ParentId, c.DisplayOrder });

        // A category with children cannot be deleted; children must be moved or deleted first.
        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_BasePrice", "[BasePrice] >= 0");
            table.HasCheckConstraint("CK_Products_DiscountPrice", "[DiscountPrice] IS NULL OR [DiscountPrice] >= 0");
            table.HasCheckConstraint("CK_Products_AverageRating", "[AverageRating] >= 0 AND [AverageRating] <= 5");
        });

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(220).IsRequired();
        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ShortDescription).HasMaxLength(500);
        builder.Property(p => p.Description);
        builder.Property(p => p.AverageRating).HasPrecision(3, 2);
        builder.Property(p => p.WeightKg).HasPrecision(10, 2);
        builder.Property(p => p.Origin).HasMaxLength(150);
        builder.Property(p => p.CareInstructions).HasMaxLength(1000);
        builder.Property(p => p.MetaTitle).HasMaxLength(200);
        builder.Property(p => p.MetaDescription).HasMaxLength(500);
        builder.Property(p => p.DeletedBy).HasMaxLength(256);
        builder.Property(p => p.SearchText).HasMaxLength(1000).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.ConfigureAudit();

        // Soft-deleted products keep their slug/SKU but free them for reuse.
        builder.HasIndex(p => p.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(p => p.Sku).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(p => new { p.Status, p.CategoryId });
        builder.HasIndex(p => p.FurnitureType);
        builder.HasIndex(p => p.IsFeatured);
        builder.HasIndex(p => p.PublishedAt);
        builder.HasIndex(p => p.SoldCount);
        builder.HasIndex(p => p.Name);

        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Style)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.StyleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants", table =>
        {
            table.HasCheckConstraint("CK_ProductVariants_Price", "[Price] >= 0");
            table.HasCheckConstraint("CK_ProductVariants_OriginalPrice", "[OriginalPrice] IS NULL OR [OriginalPrice] >= 0");
            table.HasCheckConstraint("CK_ProductVariants_Stock", "[StockQuantity] >= 0");
        });

        builder.Property(v => v.Name).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Sku).HasMaxLength(60).IsRequired();
        builder.Property(v => v.WeightKg).HasPrecision(10, 2);
        builder.Property(v => v.Version).IsConcurrencyToken();
        builder.ConfigureAudit();

        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => new { v.ProductId, v.IsActive, v.DisplayOrder });

        // Variants of a soft-deleted product are hidden together with it.
        builder.HasQueryFilter(v => !v.Product.IsDeleted);

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Style)
            .WithMany(s => s.Variants)
            .HasForeignKey(v => v.StyleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");

        builder.Property(i => i.Url).HasMaxLength(500).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(250);

        builder.HasIndex(i => new { i.ProductId, i.DisplayOrder });
        builder.HasQueryFilter(i => !i.Product.IsDeleted);

        // Products are soft-deleted, so the product FK never cascades (and SQL Server forbids two cascade
        // paths Product→Image and Product→Variant→Image). Deleting a variant turns its images into product images.
        builder.HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductVariant)
            .WithMany(v => v.Images)
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProductColorConfiguration : IEntityTypeConfiguration<ProductColor>
{
    public void Configure(EntityTypeBuilder<ProductColor> builder)
    {
        builder.ToTable("ProductColors");
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        builder.Property(c => c.HexCode).HasMaxLength(7).IsRequired().IsFixedLength(false);
        builder.ConfigureAudit();

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class ProductMaterialConfiguration : IEntityTypeConfiguration<ProductMaterial>
{
    public void Configure(EntityTypeBuilder<ProductMaterial> builder)
    {
        builder.ToTable("ProductMaterials");
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Slug).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.ConfigureAudit();

        builder.HasIndex(m => m.Slug).IsUnique();
        builder.HasIndex(m => m.Name).IsUnique();
    }
}

internal sealed class ProductSizeConfiguration : IEntityTypeConfiguration<ProductSize>
{
    public void Configure(EntityTypeBuilder<ProductSize> builder)
    {
        builder.ToTable("ProductSizes", table =>
        {
            table.HasCheckConstraint("CK_ProductSizes_Dimensions", "[LengthMm] > 0 AND [WidthMm] > 0 AND [HeightMm] > 0");
        });
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(120).IsRequired();
        builder.ConfigureAudit();

        builder.HasIndex(s => s.Slug).IsUnique();
        builder.HasIndex(s => s.FurnitureType);
    }
}

internal sealed class ProductStyleConfiguration : IEntityTypeConfiguration<ProductStyle>
{
    public void Configure(EntityTypeBuilder<ProductStyle> builder)
    {
        builder.ToTable("ProductStyles");
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.ImageUrl).HasMaxLength(500);
        builder.ConfigureAudit();

        builder.HasIndex(s => s.Slug).IsUnique();
        builder.HasIndex(s => s.Code).IsUnique();
    }
}

internal sealed class ProductVariantColorConfiguration : IEntityTypeConfiguration<ProductVariantColor>
{
    public void Configure(EntityTypeBuilder<ProductVariantColor> builder)
    {
        builder.ToTable("ProductVariantColors");
        builder.HasKey(x => new { x.ProductVariantId, x.ColorId });
        builder.Property(x => x.Part).HasMaxLength(60);
        builder.HasIndex(x => x.ColorId);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);

        builder.HasOne(x => x.ProductVariant).WithMany(v => v.Colors).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Color).WithMany(c => c.VariantColors).HasForeignKey(x => x.ColorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductVariantMaterialConfiguration : IEntityTypeConfiguration<ProductVariantMaterial>
{
    public void Configure(EntityTypeBuilder<ProductVariantMaterial> builder)
    {
        builder.ToTable("ProductVariantMaterials");
        builder.HasKey(x => new { x.ProductVariantId, x.MaterialId });
        builder.Property(x => x.Part).HasMaxLength(60);
        builder.HasIndex(x => x.MaterialId);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);

        builder.HasOne(x => x.ProductVariant).WithMany(v => v.Materials).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Material).WithMany(m => m.VariantMaterials).HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductVariantSizeConfiguration : IEntityTypeConfiguration<ProductVariantSize>
{
    public void Configure(EntityTypeBuilder<ProductVariantSize> builder)
    {
        builder.ToTable("ProductVariantSizes");
        builder.HasKey(x => new { x.ProductVariantId, x.SizeId });
        builder.Property(x => x.Part).HasMaxLength(60);
        builder.HasIndex(x => x.SizeId);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);

        builder.HasOne(x => x.ProductVariant).WithMany(v => v.Sizes).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Size).WithMany(s => s.VariantSizes).HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.ToTable("ProductPriceHistory");
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.Property(h => h.ChangedBy).HasMaxLength(256);
        builder.HasIndex(h => new { h.ProductId, h.ChangedAt });
        builder.HasQueryFilter(h => !h.Product.IsDeleted);

        builder.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ProductVariant).WithMany().HasForeignKey(h => h.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("PriceRules", table =>
        {
            table.HasCheckConstraint("CK_PriceRules_Value", "[Value] >= 0");
        });
        builder.Property(r => r.Code).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Value).HasPrecision(18, 4);
        builder.Property(r => r.Unit).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.ConfigureAudit();

        builder.HasIndex(r => r.Code).IsUnique();
        builder.HasIndex(r => new { r.RuleType, r.IsActive });

        builder.HasOne(r => r.Material).WithMany().HasForeignKey(r => r.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}
