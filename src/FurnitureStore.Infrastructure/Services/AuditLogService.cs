using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Services;

public sealed class AuditLogService(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    private static readonly string[] SensitiveFragments =
        ["password", "hash", "token", "secret", "apikey", "securitystamp", "concurrencystamp", "otp", "cvv", "cardnumber"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        AuditLog? log = null;
        try
        {
            log = context.AuditLogs.Add(new AuditLog
            {
                Action = entry.Action,
                EntityName = entry.EntityName,
                EntityId = entry.EntityId,
                Description = Truncate(entry.Description, 1000),
                OldValues = Serialize(entry.OldValues),
                NewValues = Serialize(entry.NewValues),
                UserId = entry.UserId ?? currentUser.UserId,
                UserName = entry.UserName ?? currentUser.UserName,
                IpAddress = currentUser.IpAddress,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime
            }).Entity;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Auditing must never break the business operation that already succeeded.
            if (log is not null)
            {
                context.Entry(log).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
            logger.LogError(ex, "Failed to write audit log {Action} {Entity} {EntityId}", entry.Action, entry.EntityName, entry.EntityId);
        }
    }

    /// <summary>Serializes values to JSON with every sensitive property removed (recursively).</summary>
    internal static string? Serialize(object? values)
    {
        if (values is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(values, JsonOptions);
        Scrub(node);
        return node?.ToJsonString(JsonOptions);
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var normalized = key.Replace("_", string.Empty).ToLowerInvariant();
                    if (SensitiveFragments.Any(normalized.Contains))
                    {
                        obj.Remove(key);
                    }
                    else
                    {
                        Scrub(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Scrub(item);
                }
                break;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
