using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Application.Catalog.Admin;

public interface IProductAdminService
{
    Task<PagedResult<AdminProductListItemDto>> ListAsync(AdminProductQuery query, CancellationToken cancellationToken = default);
    Task<ProductEditDto> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductDetailDto> GetDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductSavedResult> CreateAsync(ProductUpsertCommand command, CancellationToken cancellationToken = default);
    Task<ProductSavedResult> UpdateAsync(int id, ProductUpsertCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task SetStatusAsync(int id, ProductStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductImageDto>> AddImagesAsync(int productId, IReadOnlyList<ImageUpload> uploads, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(int productId, int imageId, CancellationToken cancellationToken = default);
    Task SetPrimaryImageAsync(int productId, int imageId, CancellationToken cancellationToken = default);
    Task<CatalogLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default);
}

public sealed class ProductAdminService(
    IProductRepository products,
    ICategoryRepository categories,
    IRepository<ProductColor> colors,
    IRepository<ProductMaterial> materials,
    IRepository<ProductSize> sizes,
    IRepository<ProductStyle> styles,
    IRepository<ProductVariant> variants,
    IRepository<ProductImage> images,
    IRepository<ProductPriceHistory> priceHistory,
    IUnitOfWork unitOfWork,
    IFileStorageService fileStorage,
    IValidator<ProductUpsertCommand> validator,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    CatalogCache catalogCache,
    TimeProvider timeProvider,
    ILogger<ProductAdminService> logger) : IProductAdminService
{
    public const int MaxImagesPerProduct = 30;
    public const int MaxImagesPerUpload = 10;
    private const string ImageFolder = "products";

    public Task<PagedResult<AdminProductListItemDto>> ListAsync(AdminProductQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return products.SearchAdminAsync(query, cancellationToken);
    }

    public async Task<ProductDetailDto> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetDetailByIdAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id);
        return CatalogMapper.ToDetail(product);
    }

    public async Task<ProductEditDto> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetDetailByIdAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id);

        var command = new ProductUpsertCommand
        {
            Name = product.Name,
            Slug = product.Slug,
            Sku = product.Sku,
            CategoryId = product.CategoryId,
            StyleId = product.StyleId,
            FurnitureType = product.FurnitureType,
            Status = product.Status,
            IsFeatured = product.IsFeatured,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            BasePrice = product.BasePrice,
            DiscountPrice = product.DiscountPrice,
            StockQuantity = product.StockQuantity,
            LengthMm = product.LengthMm,
            WidthMm = product.WidthMm,
            HeightMm = product.HeightMm,
            WeightKg = product.WeightKg,
            WarrantyMonths = product.WarrantyMonths,
            Origin = product.Origin,
            CareInstructions = product.CareInstructions,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            Version = product.Version,
            Variants = product.Variants.OrderBy(v => v.DisplayOrder).ThenBy(v => v.Id).Select(v =>
            {
                var secondaryMaterial = v.Materials.FirstOrDefault(m => !m.IsPrimary);
                var secondaryColor = v.Colors.FirstOrDefault(c => !c.IsPrimary);
                return new VariantUpsertModel
                {
                    Id = v.Id,
                    Name = v.Name,
                    Sku = v.Sku,
                    ColorId = v.Colors.FirstOrDefault(c => c.IsPrimary)?.ColorId,
                    MaterialId = v.Materials.FirstOrDefault(m => m.IsPrimary)?.MaterialId,
                    SizeId = v.Sizes.FirstOrDefault(s => s.IsPrimary)?.SizeId,
                    StyleId = v.StyleId,
                    Price = v.Price,
                    OriginalPrice = v.OriginalPrice,
                    StockQuantity = v.StockQuantity,
                    LowStockThreshold = v.LowStockThreshold,
                    WeightKg = v.WeightKg,
                    IsActive = v.IsActive,
                    IsDefault = v.IsDefault,
                    SecondaryMaterialId = secondaryMaterial?.MaterialId,
                    SecondaryMaterialPart = secondaryMaterial?.Part,
                    SecondaryColorId = secondaryColor?.ColorId,
                    SecondaryColorPart = secondaryColor?.Part
                };
            }).ToList()
        };

        var imageDtos = product.Images
            .OrderBy(i => i.ProductVariantId.HasValue).ThenByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
            .Select(i => new ProductImageDto(i.Id, i.Url, i.AltText, i.ProductVariantId, i.IsPrimary))
            .ToList();

        return new ProductEditDto(product.Id, command, imageDtos, product.Slug, product.CreatedAt, product.UpdatedAt, product.UpdatedBy);
    }

    public async Task<ProductSavedResult> CreateAsync(ProductUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var refs = await LoadReferencesAsync(cancellationToken);
        ValidateReferences(command, refs);

        var sku = NormalizeSku(command.Sku);
        if (await products.SkuExistsAsync(sku, null, cancellationToken))
        {
            throw new AppValidationException(nameof(command.Sku), $"SKU \"{sku}\" đã được sử dụng.");
        }

        var slug = await ResolveSlugAsync(command, null, cancellationToken);
        var variantModels = command.Variants.Count > 0 ? command.Variants : [DefaultVariantFrom(command, sku)];
        await EnsureVariantSkusAvailableAsync(variantModels, null, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var product = new Product { Slug = slug, Sku = sku };
        ApplyProductFields(product, command, now);

        var multiMaterial = variantModels.Select(v => v.MaterialId).Distinct().Count() > 1;
        var order = 0;
        foreach (var model in variantModels)
        {
            var variant = new ProductVariant { DisplayOrder = order++ };
            ApplyVariantFields(variant, model, refs, multiMaterial);
            product.Variants.Add(variant);
        }

        EnsureSingleDefault(product);
        product.SyncFromVariants();
        product.SearchText = BuildSearchText(product, refs);

        await products.AddAsync(product, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, nameof(Product), product.Id.ToString(), $"Tạo sản phẩm {product.Name}",
            NewValues: new { product.Name, product.Sku, product.Slug, product.Status, product.BasePrice, Variants = product.Variants.Count }), cancellationToken);
        logger.LogInformation("Product {ProductId} ({Sku}) created by {User}", product.Id, product.Sku, currentUser.UserName);
        catalogCache.Invalidate();

        return new ProductSavedResult(product.Id, product.Slug, product.Variants.OrderBy(v => v.DisplayOrder).Select(v => v.Id).ToList());
    }

    public async Task<ProductSavedResult> UpdateAsync(int id, ProductUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        if (command.Variants.Count == 0)
        {
            throw new AppValidationException(nameof(command.Variants), "Sản phẩm phải có ít nhất một biến thể.");
        }

        var product = await products.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id);
        if (command.Version.HasValue && command.Version.Value != product.Version)
        {
            throw new ConflictException("Sản phẩm vừa được người khác cập nhật. Vui lòng tải lại trang trước khi lưu.");
        }

        var refs = await LoadReferencesAsync(cancellationToken);
        ValidateReferences(command, refs);

        var sku = NormalizeSku(command.Sku);
        if (!string.Equals(sku, product.Sku, StringComparison.OrdinalIgnoreCase) && await products.SkuExistsAsync(sku, id, cancellationToken))
        {
            throw new AppValidationException(nameof(command.Sku), $"SKU \"{sku}\" đã được sử dụng.");
        }

        var unknownIds = command.Variants.Where(v => v.Id.HasValue && product.Variants.All(existing => existing.Id != v.Id)).ToList();
        if (unknownIds.Count > 0)
        {
            throw new AppValidationException("Biến thể không thuộc sản phẩm này.");
        }

        await EnsureVariantSkusAvailableAsync(command.Variants, id, cancellationToken);

        var before = new { product.Name, product.Sku, product.Status, product.BasePrice, product.DiscountPrice, product.CategoryId };
        var now = timeProvider.GetUtcNow().UtcDateTime;
        product.Sku = sku;
        product.Slug = await ResolveSlugAsync(command, product, cancellationToken);
        ApplyProductFields(product, command, now);

        // Variants removed from the form are deleted; order lines keep their snapshot.
        foreach (var removed in product.Variants.Where(v => command.Variants.All(m => m.Id != v.Id)).ToList())
        {
            product.Variants.Remove(removed);
            variants.Remove(removed);
        }

        var multiMaterial = command.Variants.Select(v => v.MaterialId).Distinct().Count() > 1;
        var order = 0;
        foreach (var model in command.Variants)
        {
            var variant = model.Id.HasValue ? product.Variants.Single(v => v.Id == model.Id) : new ProductVariant();
            if (model.Id.HasValue)
            {
                await RecordPriceChangeAsync(product, variant, model, now, cancellationToken);
            }
            else
            {
                product.Variants.Add(variant);
            }

            variant.DisplayOrder = order++;
            ApplyVariantFields(variant, model, refs, multiMaterial);
        }

        EnsureSingleDefault(product);
        product.SyncFromVariants();
        product.SearchText = BuildSearchText(product, refs);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Product), product.Id.ToString(), $"Cập nhật sản phẩm {product.Name}",
            OldValues: before,
            NewValues: new { product.Name, product.Sku, product.Status, product.BasePrice, product.DiscountPrice, product.CategoryId, Variants = product.Variants.Count }),
            cancellationToken);
        logger.LogInformation("Product {ProductId} updated by {User}", product.Id, currentUser.UserName);
        catalogCache.Invalidate();

        return new ProductSavedResult(product.Id, product.Slug, command.Variants.Select((m, i) =>
            m.Id ?? product.Variants.Single(v => v.DisplayOrder == i).Id).ToList());
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id);

        products.Remove(product); // soft delete (see AuditableEntityInterceptor)
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(Product), id.ToString(), $"Xóa sản phẩm {product.Name}",
            OldValues: new { product.Name, product.Sku }), cancellationToken);
        logger.LogInformation("Product {ProductId} soft-deleted by {User}", id, currentUser.UserName);
        catalogCache.Invalidate();
    }

    public async Task SetStatusAsync(int id, ProductStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            throw new AppValidationException("Trạng thái không hợp lệ.");
        }

        var product = await products.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id);
        var old = product.Status;
        product.Status = status;
        if (status == ProductStatus.Active)
        {
            product.PublishedAt ??= timeProvider.GetUtcNow().UtcDateTime;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.StatusChange, nameof(Product), id.ToString(), $"Đổi trạng thái {product.Name}",
            OldValues: new { Status = old }, NewValues: new { Status = status }), cancellationToken);
        catalogCache.Invalidate();
    }

    public async Task<IReadOnlyList<ProductImageDto>> AddImagesAsync(int productId, IReadOnlyList<ImageUpload> uploads, CancellationToken cancellationToken = default)
    {
        if (uploads.Count == 0)
        {
            throw new AppValidationException("Vui lòng chọn ít nhất một ảnh.");
        }

        if (uploads.Count > MaxImagesPerUpload)
        {
            throw new AppValidationException($"Mỗi lần tải lên tối đa {MaxImagesPerUpload} ảnh.");
        }

        var product = await products.GetForUpdateAsync(productId, cancellationToken) ?? throw new NotFoundException("sản phẩm", productId);
        if (product.Images.Count + uploads.Count > MaxImagesPerProduct)
        {
            throw new AppValidationException($"Mỗi sản phẩm tối đa {MaxImagesPerProduct} ảnh.");
        }

        if (uploads.Any(u => u.VariantId.HasValue && product.Variants.All(v => v.Id != u.VariantId)))
        {
            throw new AppValidationException("Biến thể không thuộc sản phẩm này.");
        }

        var stored = new List<StoredFile>();
        try
        {
            var nextOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.DisplayOrder) + 1;
            var hasPrimary = product.Images.Any(i => i.IsPrimary);
            var added = new List<ProductImage>();

            foreach (var upload in uploads)
            {
                var file = await fileStorage.SaveImageAsync(upload.Content, upload.FileName, ImageFolder, cancellationToken);
                stored.Add(file);

                var image = new ProductImage
                {
                    ProductVariantId = upload.VariantId,
                    Url = file.Url,
                    AltText = product.Name,
                    DisplayOrder = upload.VariantId.HasValue ? 100 + nextOrder++ : nextOrder++,
                    IsPrimary = !hasPrimary && upload.VariantId is null
                };
                hasPrimary |= image.IsPrimary;
                product.Images.Add(image);
                added.Add(image);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Product), productId.ToString(), $"Thêm {added.Count} ảnh cho {product.Name}"), cancellationToken);
            catalogCache.Invalidate();

            return added.Select(i => new ProductImageDto(i.Id, i.Url, i.AltText, i.ProductVariantId, i.IsPrimary)).ToList();
        }
        catch
        {
            // Nothing was committed: remove the files already written for this request.
            foreach (var file in stored)
            {
                await fileStorage.DeleteAsync(file.Url, CancellationToken.None);
            }
            throw;
        }
    }

    public async Task DeleteImageAsync(int productId, int imageId, CancellationToken cancellationToken = default)
    {
        var product = await products.GetForUpdateAsync(productId, cancellationToken) ?? throw new NotFoundException("sản phẩm", productId);
        var image = product.Images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("ảnh", imageId);

        product.Images.Remove(image);
        images.Remove(image);
        if (image.IsPrimary)
        {
            var next = product.Images.Where(i => i.ProductVariantId is null).OrderBy(i => i.DisplayOrder).FirstOrDefault()
                       ?? product.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault();
            if (next is not null)
            {
                next.IsPrimary = true;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await fileStorage.DeleteAsync(image.Url, cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Product), productId.ToString(), $"Xóa ảnh #{imageId} của {product.Name}"), cancellationToken);
        catalogCache.Invalidate();
    }

    public async Task SetPrimaryImageAsync(int productId, int imageId, CancellationToken cancellationToken = default)
    {
        var product = await products.GetForUpdateAsync(productId, cancellationToken) ?? throw new NotFoundException("sản phẩm", productId);
        if (product.Images.All(i => i.Id != imageId))
        {
            throw new NotFoundException("ảnh", imageId);
        }

        foreach (var image in product.Images)
        {
            image.IsPrimary = image.Id == imageId;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        catalogCache.Invalidate();
    }

    public async Task<CatalogLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var refs = await LoadReferencesAsync(cancellationToken);
        return new CatalogLookupsDto(
            refs.Categories.Values.OrderBy(c => c.ParentId ?? c.Id).ThenBy(c => c.ParentId.HasValue).ThenBy(c => c.DisplayOrder)
                .Select(c => (c.Id, c.Name, c.ParentId)).ToList(),
            refs.Colors.Values.OrderBy(c => c.DisplayOrder).Select(c => new OptionDto(c.Id, c.Name, c.Slug, c.HexCode)).ToList(),
            refs.Materials.Values.OrderBy(m => m.DisplayOrder).Select(m => new OptionDto(m.Id, m.Name, m.Slug)).ToList(),
            refs.Sizes.Values.OrderBy(s => s.DisplayOrder).Select(s => new OptionDto(s.Id, s.Name, s.Slug, Description: s.DimensionsText)).ToList(),
            refs.Styles.Values.OrderBy(s => s.DisplayOrder).Select(s => new OptionDto(s.Id, s.Name, s.Slug)).ToList());
    }

    // ------------------------------------------------------------------ helpers

    private sealed record References(
        Dictionary<int, Category> Categories,
        Dictionary<int, ProductColor> Colors,
        Dictionary<int, ProductMaterial> Materials,
        Dictionary<int, ProductSize> Sizes,
        Dictionary<int, ProductStyle> Styles);

    private async Task<References> LoadReferencesAsync(CancellationToken cancellationToken) => new(
        (await categories.ListAsync(cancellationToken: cancellationToken)).ToDictionary(c => c.Id),
        (await colors.ListAsync(cancellationToken: cancellationToken)).ToDictionary(c => c.Id),
        (await materials.ListAsync(cancellationToken: cancellationToken)).ToDictionary(m => m.Id),
        (await sizes.ListAsync(cancellationToken: cancellationToken)).ToDictionary(s => s.Id),
        (await styles.ListAsync(cancellationToken: cancellationToken)).ToDictionary(s => s.Id));

    private static void ValidateReferences(ProductUpsertCommand command, References refs)
    {
        var errors = new Dictionary<string, string[]>();
        if (!refs.Categories.ContainsKey(command.CategoryId))
        {
            errors[nameof(command.CategoryId)] = ["Danh mục không tồn tại."];
        }

        if (command.StyleId.HasValue && !refs.Styles.ContainsKey(command.StyleId.Value))
        {
            errors[nameof(command.StyleId)] = ["Phong cách không tồn tại."];
        }

        for (var i = 0; i < command.Variants.Count; i++)
        {
            var v = command.Variants[i];
            var problems = new List<string>();
            if (v.ColorId.HasValue && !refs.Colors.ContainsKey(v.ColorId.Value)) problems.Add("màu");
            if (v.SecondaryColorId.HasValue && !refs.Colors.ContainsKey(v.SecondaryColorId.Value)) problems.Add("màu phụ");
            if (v.MaterialId.HasValue && !refs.Materials.ContainsKey(v.MaterialId.Value)) problems.Add("chất liệu");
            if (v.SecondaryMaterialId.HasValue && !refs.Materials.ContainsKey(v.SecondaryMaterialId.Value)) problems.Add("chất liệu phụ");
            if (v.SizeId.HasValue && !refs.Sizes.ContainsKey(v.SizeId.Value)) problems.Add("kích thước");
            if (v.StyleId.HasValue && !refs.Styles.ContainsKey(v.StyleId.Value)) problems.Add("mẫu mã");
            if (problems.Count > 0)
            {
                errors[$"Variants[{i}]"] = [$"Biến thể {i + 1}: {string.Join(", ", problems)} không tồn tại."];
            }
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private async Task<string> ResolveSlugAsync(ProductUpsertCommand command, Product? existing, CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(command.Slug) ? null : command.Slug.Trim().ToLowerInvariant();
        if (requested is not null)
        {
            if (existing is not null && requested == existing.Slug)
            {
                return requested;
            }

            if (await products.SlugExistsAsync(requested, existing?.Id, cancellationToken))
            {
                throw new AppValidationException(nameof(command.Slug), $"Slug \"{requested}\" đã được sử dụng.");
            }

            return requested;
        }

        if (existing is not null)
        {
            return existing.Slug;
        }

        var baseSlug = SlugGenerator.Generate(command.Name, 200);
        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = SlugGenerator.Generate(command.Sku, 200);
        }

        var slug = baseSlug;
        for (var suffix = 2; await products.SlugExistsAsync(slug, null, cancellationToken); suffix++)
        {
            slug = $"{baseSlug}-{suffix}";
        }

        return slug;
    }

    private async Task EnsureVariantSkusAvailableAsync(IReadOnlyCollection<VariantUpsertModel> models, int? productId, CancellationToken cancellationToken)
    {
        var skus = models.Select(v => NormalizeSku(v.Sku)).ToList();
        var inUse = await products.FindVariantSkusInUseAsync(skus, productId, cancellationToken);
        if (inUse.Count > 0)
        {
            throw new AppValidationException(nameof(ProductUpsertCommand.Variants),
                $"SKU biến thể đã được dùng cho sản phẩm khác: {string.Join(", ", inUse)}.");
        }
    }

    private static VariantUpsertModel DefaultVariantFrom(ProductUpsertCommand command, string sku) => new()
    {
        Name = "Tiêu chuẩn",
        Sku = sku,
        StyleId = command.StyleId,
        Price = command.DiscountPrice ?? command.BasePrice,
        OriginalPrice = command.DiscountPrice.HasValue ? command.BasePrice : null,
        StockQuantity = command.StockQuantity,
        WeightKg = command.WeightKg,
        IsDefault = true
    };

    private static void ApplyProductFields(Product product, ProductUpsertCommand command, DateTime now)
    {
        product.Name = command.Name.Trim();
        product.CategoryId = command.CategoryId;
        product.StyleId = command.StyleId;
        product.FurnitureType = command.FurnitureType;
        product.Status = command.Status;
        product.IsFeatured = command.IsFeatured;
        product.ShortDescription = Clean(command.ShortDescription);
        product.Description = Clean(command.Description);
        product.LengthMm = command.LengthMm;
        product.WidthMm = command.WidthMm;
        product.HeightMm = command.HeightMm;
        product.WeightKg = command.WeightKg;
        product.WarrantyMonths = command.WarrantyMonths;
        product.Origin = Clean(command.Origin);
        product.CareInstructions = Clean(command.CareInstructions);
        product.MetaTitle = Clean(command.MetaTitle);
        product.MetaDescription = Clean(command.MetaDescription);
        if (product.Status == ProductStatus.Active)
        {
            product.PublishedAt ??= now;
        }
    }

    private static void ApplyVariantFields(ProductVariant variant, VariantUpsertModel model, References refs, bool multiMaterial)
    {
        variant.Sku = NormalizeSku(model.Sku);
        variant.StyleId = model.StyleId;
        variant.Price = model.Price;
        variant.OriginalPrice = model.OriginalPrice;
        variant.StockQuantity = model.StockQuantity;
        variant.LowStockThreshold = model.LowStockThreshold;
        variant.WeightKg = model.WeightKg;
        variant.IsActive = model.IsActive;
        variant.IsDefault = model.IsDefault;

        SyncJunction(variant.Colors, model.ColorId, model.SecondaryColorId, model.SecondaryColorPart, c => c.ColorId,
            (id, primary, part) => new ProductVariantColor { ColorId = id, IsPrimary = primary, Part = part },
            (row, primary, part) => { row.IsPrimary = primary; row.Part = part; });
        SyncJunction(variant.Materials, model.MaterialId, model.SecondaryMaterialId, model.SecondaryMaterialPart, m => m.MaterialId,
            (id, primary, part) => new ProductVariantMaterial { MaterialId = id, IsPrimary = primary, Part = part },
            (row, primary, part) => { row.IsPrimary = primary; row.Part = part; });
        SyncJunction(variant.Sizes, model.SizeId, null, null, s => s.SizeId,
            (id, primary, part) => new ProductVariantSize { SizeId = id, IsPrimary = primary, Part = part },
            (row, primary, part) => { row.IsPrimary = primary; row.Part = part; });

        variant.Name = string.IsNullOrWhiteSpace(model.Name)
            ? BuildVariantName(model, refs, multiMaterial)
            : model.Name.Trim();
    }

    /// <summary>Updates junction rows in place (add / update / remove) so unchanged rows are not re-inserted.</summary>
    private static void SyncJunction<T>(ICollection<T> rows, int? primaryId, int? secondaryId, string? secondaryPart,
        Func<T, int> key, Func<int, bool, string?, T> create, Action<T, bool, string?> update)
    {
        var desired = new List<(int Id, bool Primary, string? Part)>();
        if (primaryId.HasValue)
        {
            desired.Add((primaryId.Value, true, null));
        }
        if (secondaryId.HasValue && secondaryId != primaryId)
        {
            desired.Add((secondaryId.Value, false, string.IsNullOrWhiteSpace(secondaryPart) ? null : secondaryPart.Trim()));
        }

        foreach (var row in rows.Where(r => desired.All(d => d.Id != key(r))).ToList())
        {
            rows.Remove(row);
        }

        foreach (var (id, primary, part) in desired)
        {
            var existing = rows.FirstOrDefault(r => key(r) == id);
            if (existing is null)
            {
                rows.Add(create(id, primary, part));
            }
            else
            {
                update(existing, primary, part);
            }
        }
    }

    private static string BuildVariantName(VariantUpsertModel model, References refs, bool multiMaterial)
    {
        var parts = new List<string>();
        if (multiMaterial && model.MaterialId.HasValue) parts.Add(refs.Materials[model.MaterialId.Value].Name);
        if (model.ColorId.HasValue) parts.Add(refs.Colors[model.ColorId.Value].Name);
        if (model.SizeId.HasValue) parts.Add(refs.Sizes[model.SizeId.Value].Name);
        return parts.Count > 0 ? string.Join(" / ", parts) : "Tiêu chuẩn";
    }

    private static void EnsureSingleDefault(Product product)
    {
        var active = product.Variants.Where(v => v.IsActive).ToList();
        var chosen = product.Variants.FirstOrDefault(v => v.IsDefault && v.IsActive)
                     ?? active.FirstOrDefault(v => v.IsInStock)
                     ?? active.FirstOrDefault()
                     ?? product.Variants.First();
        foreach (var variant in product.Variants)
        {
            variant.IsDefault = ReferenceEquals(variant, chosen);
        }
    }

    private async Task RecordPriceChangeAsync(Product product, ProductVariant variant, VariantUpsertModel model, DateTime now, CancellationToken cancellationToken)
    {
        if (variant.Price == model.Price && variant.OriginalPrice == model.OriginalPrice)
        {
            return;
        }

        await priceHistory.AddAsync(new ProductPriceHistory
        {
            ProductId = product.Id,
            ProductVariantId = variant.Id,
            OldPrice = variant.Price,
            NewPrice = model.Price,
            OldOriginalPrice = variant.OriginalPrice,
            NewOriginalPrice = model.OriginalPrice,
            ChangedAt = now,
            ChangedBy = currentUser.UserName,
            Reason = "Cập nhật từ trang quản trị"
        }, cancellationToken);
    }

    private static string BuildSearchText(Product product, References refs)
    {
        var category = refs.Categories[product.CategoryId];
        var parent = category.ParentId.HasValue ? refs.Categories.GetValueOrDefault(category.ParentId.Value) : null;
        var style = product.StyleId.HasValue ? refs.Styles.GetValueOrDefault(product.StyleId.Value) : null;
        var materialNames = product.Variants.SelectMany(v => v.Materials).Select(m => refs.Materials[m.MaterialId].Name);
        var colorNames = product.Variants.SelectMany(v => v.Colors).Select(c => refs.Colors[c.ColorId].Name);

        return ProductSearchText.Build(product.Name, product.Sku, category.Name, parent?.Name, style?.Name, materialNames, colorNames,
            FurnitureTypes.NameOf(product.FurnitureType));
    }

    private static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
