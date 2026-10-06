namespace FurnitureStore.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant would be violated (e.g. selling more than the stock on hand,
/// moving an order to an invalid status). The message is safe to show to end users.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
