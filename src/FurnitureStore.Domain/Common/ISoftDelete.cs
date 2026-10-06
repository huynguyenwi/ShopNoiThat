namespace FurnitureStore.Domain.Common;

/// <summary>
/// Marks an entity that is never physically removed; a global query filter hides deleted rows.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
