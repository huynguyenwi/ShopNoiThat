using FluentValidation;

namespace FurnitureStore.Application.Catalog.Admin;

internal static class CatalogRules
{
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";
    public const string SkuPattern = "^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$";
    public const string HexPattern = "^#[0-9A-Fa-f]{6}$";
    public const string SlugMessage = "Slug chỉ gồm chữ thường không dấu, chữ số và dấu gạch ngang (ví dụ: ban-an-go-soi).";
    public const string SkuMessage = "SKU chỉ gồm chữ, số và dấu gạch ngang (ví dụ: BA-OC-180).";
}

public sealed class ProductUpsertCommandValidator : AbstractValidator<ProductUpsertCommand>
{
    public const int MaxVariants = 100;

    public ProductUpsertCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên sản phẩm.")
            .Length(3, 200).WithMessage("Tên sản phẩm từ 3 đến 200 ký tự.");
        RuleFor(x => x.Slug).MaximumLength(220).Matches(CatalogRules.SlugPattern).WithMessage(CatalogRules.SlugMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Vui lòng nhập mã SKU.")
            .MaximumLength(50).WithMessage("SKU tối đa 50 ký tự.")
            .Matches(CatalogRules.SkuPattern).WithMessage(CatalogRules.SkuMessage);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Vui lòng chọn danh mục.");
        RuleFor(x => x.FurnitureType).IsInEnum().WithMessage("Loại nội thất không hợp lệ.");
        RuleFor(x => x.Status).IsInEnum().WithMessage("Trạng thái không hợp lệ.");
        RuleFor(x => x.ShortDescription).MaximumLength(500).WithMessage("Mô tả ngắn tối đa 500 ký tự.");
        RuleFor(x => x.Description).MaximumLength(20000).WithMessage("Mô tả tối đa 20.000 ký tự.");
        RuleFor(x => x.Origin).MaximumLength(150);
        RuleFor(x => x.CareInstructions).MaximumLength(1000);
        RuleFor(x => x.MetaTitle).MaximumLength(200).WithMessage("Meta title tối đa 200 ký tự.");
        RuleFor(x => x.MetaDescription).MaximumLength(500).WithMessage("Meta description tối đa 500 ký tự.");
        RuleFor(x => x.LengthMm).InclusiveBetween(1, 10_000).When(x => x.LengthMm.HasValue).WithMessage("Chiều dài từ 1 đến 10.000 mm.");
        RuleFor(x => x.WidthMm).InclusiveBetween(1, 10_000).When(x => x.WidthMm.HasValue).WithMessage("Chiều rộng từ 1 đến 10.000 mm.");
        RuleFor(x => x.HeightMm).InclusiveBetween(1, 10_000).When(x => x.HeightMm.HasValue).WithMessage("Chiều cao từ 1 đến 10.000 mm.");
        RuleFor(x => x.WeightKg).InclusiveBetween(0, 10_000).When(x => x.WeightKg.HasValue).WithMessage("Khối lượng không hợp lệ.");
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(0, 120).WithMessage("Bảo hành từ 0 đến 120 tháng.");

        When(x => x.Variants.Count == 0, () =>
        {
            RuleFor(x => x.BasePrice).GreaterThan(0).WithMessage("Giá bán phải lớn hơn 0.");
            RuleFor(x => x.DiscountPrice)
                .GreaterThan(0).WithMessage("Giá khuyến mãi phải lớn hơn 0.")
                .LessThan(x => x.BasePrice).WithMessage("Giá khuyến mãi phải nhỏ hơn giá bán.")
                .When(x => x.DiscountPrice.HasValue);
            RuleFor(x => x.StockQuantity).InclusiveBetween(0, 100_000).WithMessage("Tồn kho từ 0 đến 100.000.");
        });

        RuleFor(x => x.Variants.Count).LessThanOrEqualTo(MaxVariants).WithMessage($"Tối đa {MaxVariants} biến thể.");
        RuleForEach(x => x.Variants).SetValidator(new VariantUpsertModelValidator());
        RuleFor(x => x.Variants)
            .Must(v => v.Select(x => x.Sku.Trim().ToUpperInvariant()).Distinct().Count() == v.Count)
            .WithMessage("Các biến thể không được trùng SKU.")
            .Must(v => v.Count(x => x.IsDefault) <= 1)
            .WithMessage("Chỉ được chọn một biến thể mặc định.")
            .Must(v => v.Select(x => (x.ColorId, x.MaterialId, x.SizeId, x.StyleId)).Distinct().Count() == v.Count)
            .WithMessage("Hai biến thể không được trùng đồng thời màu, chất liệu, kích thước và mẫu mã.");
    }
}

public sealed class VariantUpsertModelValidator : AbstractValidator<VariantUpsertModel>
{
    public VariantUpsertModelValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Vui lòng nhập SKU.")
            .MaximumLength(60).WithMessage("SKU tối đa 60 ký tự.")
            .Matches(CatalogRules.SkuPattern).WithMessage(CatalogRules.SkuMessage);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Giá phải lớn hơn 0.").LessThan(100_000_000_000m);
        RuleFor(x => x.OriginalPrice).GreaterThan(x => x.Price).When(x => x.OriginalPrice.HasValue)
            .WithMessage("Giá cũ phải lớn hơn giá bán.");
        RuleFor(x => x.StockQuantity).InclusiveBetween(0, 100_000).WithMessage("Tồn kho từ 0 đến 100.000.");
        RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 1_000).WithMessage("Ngưỡng sắp hết hàng từ 0 đến 1.000.");
        RuleFor(x => x.WeightKg).InclusiveBetween(0, 10_000).When(x => x.WeightKg.HasValue);
        RuleFor(x => x.SecondaryMaterialPart).MaximumLength(60);
        RuleFor(x => x.SecondaryColorPart).MaximumLength(60);
    }
}

public sealed class CategoryUpsertCommandValidator : AbstractValidator<CategoryUpsertCommand>
{
    public CategoryUpsertCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên danh mục.").MaximumLength(150);
        RuleFor(x => x.Slug).MaximumLength(170).Matches(CatalogRules.SlugPattern).WithMessage(CatalogRules.SlugMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.IconCssClass).MaximumLength(60).Matches("^bi-[a-z0-9-]+$").When(x => !string.IsNullOrWhiteSpace(x.IconCssClass))
            .WithMessage("Icon phải là class Bootstrap Icons, ví dụ: bi-lamp.");
        RuleFor(x => x.DisplayOrder).InclusiveBetween(0, 10_000);
        RuleFor(x => x.MetaTitle).MaximumLength(200);
        RuleFor(x => x.MetaDescription).MaximumLength(500);
    }
}

public sealed class AttributeUpsertCommandValidator : AbstractValidator<(AttributeKind Kind, AttributeUpsertCommand Command)>
{
    public AttributeUpsertCommandValidator()
    {
        RuleFor(x => x.Command.Name).NotEmpty().WithMessage("Vui lòng nhập tên.").MaximumLength(100).OverridePropertyName("Name");
        RuleFor(x => x.Command.Slug).MaximumLength(120).Matches(CatalogRules.SlugPattern).WithMessage(CatalogRules.SlugMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Command.Slug)).OverridePropertyName("Slug");
        RuleFor(x => x.Command.DisplayOrder).InclusiveBetween(0, 10_000).OverridePropertyName("DisplayOrder");
        RuleFor(x => x.Command.Description).MaximumLength(1000).OverridePropertyName("Description");

        When(x => x.Kind == AttributeKind.Color, () =>
            RuleFor(x => x.Command.HexCode).NotEmpty().WithMessage("Vui lòng chọn mã màu.")
                .Matches(CatalogRules.HexPattern).WithMessage("Mã màu phải có dạng #RRGGBB.").OverridePropertyName("HexCode"));

        When(x => x.Kind == AttributeKind.Material, () =>
            RuleFor(x => x.Command.MaterialGroup).NotNull().WithMessage("Vui lòng chọn nhóm chất liệu.").IsInEnum().OverridePropertyName("MaterialGroup"));

        When(x => x.Kind == AttributeKind.Size, () =>
        {
            RuleFor(x => x.Command.LengthMm).NotNull().InclusiveBetween(1, 10_000).WithMessage("Chiều dài từ 1 đến 10.000 mm.").OverridePropertyName("LengthMm");
            RuleFor(x => x.Command.WidthMm).NotNull().InclusiveBetween(1, 10_000).WithMessage("Chiều rộng từ 1 đến 10.000 mm.").OverridePropertyName("WidthMm");
            RuleFor(x => x.Command.HeightMm).NotNull().InclusiveBetween(1, 10_000).WithMessage("Chiều cao từ 1 đến 10.000 mm.").OverridePropertyName("HeightMm");
        });

        When(x => x.Kind == AttributeKind.Style, () =>
            RuleFor(x => x.Command.Code).NotEmpty().WithMessage("Vui lòng nhập mã phong cách (tiếng Anh).")
                .Matches("^[A-Za-z][A-Za-z0-9]{1,49}$").WithMessage("Mã phong cách chỉ gồm chữ và số, ví dụ: Scandinavian.")
                .OverridePropertyName("Code"));
    }
}
