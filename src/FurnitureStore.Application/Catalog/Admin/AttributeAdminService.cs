using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog.Admin;

public interface IAttributeAdminService
{
    Task<IReadOnlyList<AdminAttributeDto>> ListAsync(AttributeKind kind, CancellationToken cancellationToken = default);
    Task<AdminAttributeDto> GetAsync(AttributeKind kind, int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(AttributeKind kind, AttributeUpsertCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(AttributeKind kind, int id, AttributeUpsertCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(AttributeKind kind, int id, CancellationToken cancellationToken = default);
}

/// <summary>CRUD for colors, materials, sizes and styles ("mẫu mã").</summary>
public sealed class AttributeAdminService(
    IRepository<ProductColor> colors,
    IRepository<ProductMaterial> materials,
    IRepository<ProductSize> sizes,
    IRepository<ProductStyle> styles,
    ICatalogAdminRepository adminRepository,
    IUnitOfWork unitOfWork,
    IValidator<(AttributeKind Kind, AttributeUpsertCommand Command)> validator,
    IAuditLogService auditLog,
    CatalogCache catalogCache) : IAttributeAdminService
{
    public static string DisplayName(AttributeKind kind) => kind switch
    {
        AttributeKind.Color => "màu sắc",
        AttributeKind.Material => "chất liệu",
        AttributeKind.Size => "kích thước",
        AttributeKind.Style => "mẫu mã / phong cách",
        _ => "thuộc tính"
    };

    public async Task<IReadOnlyList<AdminAttributeDto>> ListAsync(AttributeKind kind, CancellationToken cancellationToken = default)
    {
        var usage = await adminRepository.GetAttributeUsageAsync(kind, cancellationToken);
        int Used(int id) => usage.GetValueOrDefault(id);

        return kind switch
        {
            AttributeKind.Color => (await colors.ListAsync(cancellationToken: cancellationToken)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new AdminAttributeDto(x.Id, kind, x.Name, x.Slug, x.DisplayOrder, x.IsActive, Used(x.Id), null, x.HexCode, null, null, null, null, null, null)).ToList(),
            AttributeKind.Material => (await materials.ListAsync(cancellationToken: cancellationToken)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new AdminAttributeDto(x.Id, kind, x.Name, x.Slug, x.DisplayOrder, x.IsActive, Used(x.Id), x.Description, null, x.Group, null, null, null, null, null)).ToList(),
            AttributeKind.Size => (await sizes.ListAsync(cancellationToken: cancellationToken)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new AdminAttributeDto(x.Id, kind, x.Name, x.Slug, x.DisplayOrder, x.IsActive, Used(x.Id), null, null, null, x.LengthMm, x.WidthMm, x.HeightMm, x.FurnitureType, null)).ToList(),
            AttributeKind.Style => (await styles.ListAsync(cancellationToken: cancellationToken)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new AdminAttributeDto(x.Id, kind, x.Name, x.Slug, x.DisplayOrder, x.IsActive, Used(x.Id), x.Description, null, null, null, null, null, null, x.Code)).ToList(),
            _ => throw new AppValidationException("Loại thuộc tính không hợp lệ.")
        };
    }

    public async Task<AdminAttributeDto> GetAsync(AttributeKind kind, int id, CancellationToken cancellationToken = default) =>
        (await ListAsync(kind, cancellationToken)).FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException(DisplayName(kind), id);

    public async Task<int> CreateAsync(AttributeKind kind, AttributeUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync((kind, command), cancellationToken);
        var existing = await ListAsync(kind, cancellationToken);
        var slug = ResolveSlug(command, null, existing);
        EnsureUniqueName(command, null, existing, kind);

        int id;
        switch (kind)
        {
            case AttributeKind.Color:
                var color = new ProductColor { Slug = slug };
                ApplyColor(color, command);
                await colors.AddAsync(color, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                id = color.Id;
                break;
            case AttributeKind.Material:
                var material = new ProductMaterial { Slug = slug };
                ApplyMaterial(material, command);
                await materials.AddAsync(material, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                id = material.Id;
                break;
            case AttributeKind.Size:
                var size = new ProductSize { Slug = slug };
                ApplySize(size, command);
                await sizes.AddAsync(size, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                id = size.Id;
                break;
            case AttributeKind.Style:
                var style = new ProductStyle { Slug = slug };
                ApplyStyle(style, command);
                await styles.AddAsync(style, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                id = style.Id;
                break;
            default:
                throw new AppValidationException("Loại thuộc tính không hợp lệ.");
        }

        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, kind.ToString(), id.ToString(), $"Tạo {DisplayName(kind)} {command.Name}", NewValues: command), cancellationToken);
        catalogCache.Invalidate();
        return id;
    }

    public async Task UpdateAsync(AttributeKind kind, int id, AttributeUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync((kind, command), cancellationToken);
        var existing = await ListAsync(kind, cancellationToken);
        var current = existing.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException(DisplayName(kind), id);
        var slug = ResolveSlug(command, current, existing);
        EnsureUniqueName(command, current, existing, kind);

        switch (kind)
        {
            case AttributeKind.Color:
                var color = (await colors.GetByIdAsync(id, cancellationToken))!;
                color.Slug = slug;
                ApplyColor(color, command);
                break;
            case AttributeKind.Material:
                var material = (await materials.GetByIdAsync(id, cancellationToken))!;
                material.Slug = slug;
                ApplyMaterial(material, command);
                break;
            case AttributeKind.Size:
                var size = (await sizes.GetByIdAsync(id, cancellationToken))!;
                size.Slug = slug;
                ApplySize(size, command);
                break;
            case AttributeKind.Style:
                var style = (await styles.GetByIdAsync(id, cancellationToken))!;
                style.Slug = slug;
                ApplyStyle(style, command);
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, kind.ToString(), id.ToString(), $"Cập nhật {DisplayName(kind)} {command.Name}",
            OldValues: current, NewValues: command), cancellationToken);
        catalogCache.Invalidate();
    }

    public async Task DeleteAsync(AttributeKind kind, int id, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(kind, id, cancellationToken);
        if (current.UsageCount > 0)
        {
            throw new BusinessRuleException(
                $"Không thể xóa {DisplayName(kind)} \"{current.Name}\" vì đang được dùng ở {current.UsageCount} nơi. Bạn có thể chuyển sang trạng thái ẩn.");
        }

        switch (kind)
        {
            case AttributeKind.Color: colors.Remove((await colors.GetByIdAsync(id, cancellationToken))!); break;
            case AttributeKind.Material: materials.Remove((await materials.GetByIdAsync(id, cancellationToken))!); break;
            case AttributeKind.Size: sizes.Remove((await sizes.GetByIdAsync(id, cancellationToken))!); break;
            case AttributeKind.Style: styles.Remove((await styles.GetByIdAsync(id, cancellationToken))!); break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, kind.ToString(), id.ToString(), $"Xóa {DisplayName(kind)} {current.Name}"), cancellationToken);
        catalogCache.Invalidate();
    }

    private static string ResolveSlug(AttributeUpsertCommand command, AdminAttributeDto? current, IReadOnlyList<AdminAttributeDto> existing)
    {
        var slug = string.IsNullOrWhiteSpace(command.Slug) ? SlugGenerator.Generate(command.Name, 110) : command.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            throw new AppValidationException(nameof(AttributeUpsertCommand.Slug), "Không tạo được slug từ tên, vui lòng nhập slug.");
        }

        if (existing.Any(x => x.Slug == slug && x.Id != current?.Id))
        {
            throw new AppValidationException(nameof(AttributeUpsertCommand.Slug), $"Slug \"{slug}\" đã được sử dụng.");
        }

        return slug;
    }

    private static void EnsureUniqueName(AttributeUpsertCommand command, AdminAttributeDto? current, IReadOnlyList<AdminAttributeDto> existing, AttributeKind kind)
    {
        if (kind is AttributeKind.Color or AttributeKind.Material
            && existing.Any(x => string.Equals(x.Name, command.Name.Trim(), StringComparison.OrdinalIgnoreCase) && x.Id != current?.Id))
        {
            throw new AppValidationException(nameof(AttributeUpsertCommand.Name), $"Tên \"{command.Name.Trim()}\" đã tồn tại.");
        }

        if (kind == AttributeKind.Style
            && existing.Any(x => string.Equals(x.Code, command.Code?.Trim(), StringComparison.OrdinalIgnoreCase) && x.Id != current?.Id))
        {
            throw new AppValidationException(nameof(AttributeUpsertCommand.Code), $"Mã \"{command.Code}\" đã tồn tại.");
        }
    }

    private static void ApplyColor(ProductColor color, AttributeUpsertCommand c)
    {
        color.Name = c.Name.Trim();
        color.HexCode = c.HexCode!.ToUpperInvariant();
        color.DisplayOrder = c.DisplayOrder;
        color.IsActive = c.IsActive;
    }

    private static void ApplyMaterial(ProductMaterial material, AttributeUpsertCommand c)
    {
        material.Name = c.Name.Trim();
        material.Group = c.MaterialGroup!.Value;
        material.Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim();
        material.DisplayOrder = c.DisplayOrder;
        material.IsActive = c.IsActive;
    }

    private static void ApplySize(ProductSize size, AttributeUpsertCommand c)
    {
        size.Name = c.Name.Trim();
        size.LengthMm = c.LengthMm!.Value;
        size.WidthMm = c.WidthMm!.Value;
        size.HeightMm = c.HeightMm!.Value;
        size.FurnitureType = c.FurnitureType;
        size.DisplayOrder = c.DisplayOrder;
        size.IsActive = c.IsActive;
    }

    private static void ApplyStyle(ProductStyle style, AttributeUpsertCommand c)
    {
        style.Name = c.Name.Trim();
        style.Code = c.Code!.Trim();
        style.Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim();
        style.DisplayOrder = c.DisplayOrder;
        style.IsActive = c.IsActive;
    }
}
