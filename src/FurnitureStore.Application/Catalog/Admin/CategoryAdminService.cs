using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog.Admin;

public interface ICategoryAdminService
{
    Task<IReadOnlyList<AdminCategoryDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AdminCategoryDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(CategoryUpsertCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, CategoryUpsertCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>Two-level category tree: rooms (top level) and product groups (children).</summary>
public sealed class CategoryAdminService(
    ICategoryRepository categories,
    ICatalogAdminRepository adminRepository,
    IUnitOfWork unitOfWork,
    IValidator<CategoryUpsertCommand> validator,
    IAuditLogService auditLog,
    CatalogCache catalogCache) : ICategoryAdminService
{
    public async Task<IReadOnlyList<AdminCategoryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await categories.GetAllOrderedAsync(cancellationToken);
        var counts = await categories.GetActiveProductCountsAsync(cancellationToken);

        AdminCategoryDto Map(Category c) => new(
            c.Id, c.Name, c.Slug, c.ParentId, all.FirstOrDefault(p => p.Id == c.ParentId)?.Name, c.Description, c.IconCssClass,
            c.DisplayOrder, c.IsActive, c.ShowOnHomePage, c.MetaTitle, c.MetaDescription,
            counts.GetValueOrDefault(c.Id), all.Count(x => x.ParentId == c.Id));

        // Parents followed by their children.
        return all.Where(c => c.ParentId is null)
            .SelectMany(parent => new[] { Map(parent) }.Concat(all.Where(c => c.ParentId == parent.Id).Select(Map)))
            .ToList();
    }

    public async Task<AdminCategoryDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException("danh mục", id);

    public async Task<int> CreateAsync(CategoryUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var all = await categories.GetAllOrderedAsync(cancellationToken);
        ValidateParent(command.ParentId, null, all);

        var category = new Category { Slug = ResolveSlug(command, null, all) };
        Apply(category, command);
        await categories.AddAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, nameof(Category), category.Id.ToString(), $"Tạo danh mục {category.Name}",
            NewValues: command), cancellationToken);
        catalogCache.Invalidate();
        return category.Id;
    }

    public async Task UpdateAsync(int id, CategoryUpsertCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var all = await categories.GetAllOrderedAsync(cancellationToken);
        var category = await categories.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("danh mục", id);
        ValidateParent(command.ParentId, category, all);

        var before = new { category.Name, category.Slug, category.ParentId, category.IsActive };
        category.Slug = ResolveSlug(command, category, all);
        Apply(category, command);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Category), id.ToString(), $"Cập nhật danh mục {category.Name}",
            OldValues: before, NewValues: new { category.Name, category.Slug, category.ParentId, category.IsActive }), cancellationToken);
        catalogCache.Invalidate();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("danh mục", id);

        if (await categories.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            throw new BusinessRuleException("Không thể xóa danh mục đang có danh mục con. Hãy chuyển hoặc xóa danh mục con trước.");
        }

        var productCount = await adminRepository.CountAllProductsInCategoryAsync(id, cancellationToken);
        if (productCount > 0)
        {
            throw new BusinessRuleException($"Không thể xóa danh mục đang chứa {productCount} sản phẩm (kể cả sản phẩm đã xóa). Hãy chuyển sản phẩm sang danh mục khác hoặc ẩn danh mục.");
        }

        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(Category), id.ToString(), $"Xóa danh mục {category.Name}"), cancellationToken);
        catalogCache.Invalidate();
    }

    private static void ValidateParent(int? parentId, Category? current, IReadOnlyList<Category> all)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        var parent = all.FirstOrDefault(c => c.Id == parentId) ?? throw new AppValidationException(nameof(CategoryUpsertCommand.ParentId), "Danh mục cha không tồn tại.");
        if (current is not null && parent.Id == current.Id)
        {
            throw new AppValidationException(nameof(CategoryUpsertCommand.ParentId), "Danh mục không thể là cha của chính nó.");
        }

        if (parent.ParentId.HasValue)
        {
            throw new AppValidationException(nameof(CategoryUpsertCommand.ParentId), "Chỉ hỗ trợ 2 cấp: danh mục cha phải là danh mục gốc (phòng).");
        }

        if (current is not null && all.Any(c => c.ParentId == current.Id))
        {
            throw new AppValidationException(nameof(CategoryUpsertCommand.ParentId), "Danh mục đang có danh mục con nên không thể trở thành danh mục con.");
        }
    }

    private static string ResolveSlug(CategoryUpsertCommand command, Category? current, IReadOnlyList<Category> all)
    {
        var slug = string.IsNullOrWhiteSpace(command.Slug) ? SlugGenerator.Generate(command.Name, 160) : command.Slug.Trim().ToLowerInvariant();
        if (all.Any(c => c.Slug == slug && c.Id != current?.Id))
        {
            throw new AppValidationException(nameof(CategoryUpsertCommand.Slug), $"Slug \"{slug}\" đã được sử dụng.");
        }

        return slug;
    }

    private static void Apply(Category category, CategoryUpsertCommand command)
    {
        category.Name = command.Name.Trim();
        category.ParentId = command.ParentId;
        category.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        category.IconCssClass = string.IsNullOrWhiteSpace(command.IconCssClass) ? null : command.IconCssClass.Trim();
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;
        category.ShowOnHomePage = command.ShowOnHomePage;
        category.MetaTitle = string.IsNullOrWhiteSpace(command.MetaTitle) ? null : command.MetaTitle.Trim();
        category.MetaDescription = string.IsNullOrWhiteSpace(command.MetaDescription) ? null : command.MetaDescription.Trim();
    }
}
