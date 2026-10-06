namespace FurnitureStore.Domain.Common;

/// <summary>
/// Entity protected by optimistic concurrency. The persistence layer regenerates <see cref="Version"/>
/// on every insert/update and uses it as a concurrency token, so two admins editing the same
/// order or two customers buying the last item cannot silently overwrite each other.
/// </summary>
public interface IConcurrencyAware
{
    Guid Version { get; set; }
}
