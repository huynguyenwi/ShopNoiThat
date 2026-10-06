using System.Text.Json;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Entities;

namespace FurnitureStore.Application.AI;

/// <summary>Tolerant reading of the JSON a model returns (may be wrapped in ```json fences or surrounded by text).</summary>
public static class ModelJson
{
    public static bool TryParse(string? content, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Int(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : [];
}

/// <summary>Builds a conversation with its messages and product cards (current catalog data) for customers and admins.</summary>
public static class AiConversationReader
{
    public static async Task<AiConversationDto> ReadAsync(IAiConversationRepository conversations, IProductFactRepository facts, int conversationId, CancellationToken cancellationToken)
    {
        var header = await conversations.GetListItemAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken);

        var metadata = messages.ToDictionary(m => m.Id, Metadata);
        var productIds = metadata.Values.Where(m => m is not null).SelectMany(m => m!.ProductIds).Distinct().ToList();
        var products = (await facts.GetFactsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);

        var items = messages.Select(m =>
        {
            var meta = metadata[m.Id];
            var cards = meta is null
                ? []
                : meta.ProductIds.Where(products.ContainsKey)
                    .Select(id => AssistantService.ToCard(products[id], meta.Reasons.GetValueOrDefault(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? string.Empty))
                    .ToList();
            return new AiMessageDto(m.Id, m.Role, m.Content, cards, meta?.Suggestions ?? [], m.IsError, m.Model, m.PromptTokens, m.CompletionTokens, m.CreatedAt);
        }).ToList();

        return new AiConversationDto(header, items);
    }

    private static AiMessageMetadata? Metadata(AIMessage message)
    {
        if (string.IsNullOrEmpty(message.MetadataJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AiMessageMetadata>(message.MetadataJson, AiMessageMetadata.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
