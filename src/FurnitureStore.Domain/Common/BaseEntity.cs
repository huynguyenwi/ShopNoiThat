namespace FurnitureStore.Domain.Common;

/// <summary>
/// Base type for every persisted entity with an integer surrogate key.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
}
