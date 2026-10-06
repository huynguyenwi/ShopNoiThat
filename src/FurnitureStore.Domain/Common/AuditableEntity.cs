namespace FurnitureStore.Domain.Common;

/// <summary>
/// Entity that tracks who created / last modified it and when (UTC).
/// Values are populated automatically by the persistence layer on SaveChanges.
/// </summary>
public abstract class AuditableEntity : BaseEntity, IAuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
