using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Common.Interfaces;

public sealed record AuditEntry(
    AuditAction Action,
    string EntityName,
    string? EntityId = null,
    string? Description = null,
    object? OldValues = null,
    object? NewValues = null,
    string? UserId = null,
    string? UserName = null);

/// <summary>
/// Writes the audit trail (logins, admin changes, order status changes...).
/// Sensitive values (passwords, hashes, tokens, keys) are stripped before saving.
/// </summary>
public interface IAuditLogService
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
