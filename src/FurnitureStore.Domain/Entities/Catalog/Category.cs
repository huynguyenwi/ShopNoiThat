using FurnitureStore.Domain.Common;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// Two-level category tree: rooms at the top ("Phòng khách") and product groups below ("Sofa", "Bàn trà").
/// </summary>
public class Category : AuditableEntity
{
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? IconCssClass { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHomePage { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
