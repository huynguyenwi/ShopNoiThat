using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.AI;

/// <summary>
/// Chat Completions client for OpenAI and OpenAI-compatible APIs (Azure OpenAI proxy, Groq, OpenRouter, Ollama, LM Studio...).
/// The API key comes from configuration (user-secrets / environment variable) and is never logged.
/// </summary>
public sealed class OpenAiCompatibleChatClient(HttpClient http, IOptionsMonitor<AiSettings> options, ILogger<OpenAiCompatibleChatClient> logger) : IAiChatClient
{
    public bool IsConfigured => options.CurrentValue.IsConfigured;

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new AiUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["messages"] = request.Messages.Select(m => new { role = RoleName(m.Role), content = m.Content }).ToList(),
            [settings.TokenLimitParameter] = settings.MaxOutputTokens
        };
        if (settings.SendTemperature) body["temperature"] = settings.Temperature;
        if (request.JsonResponse && settings.JsonMode) body["response_format"] = new { type = "json_object" };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.BaseUrl)) { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        HttpResponseMessage response;
        var started = DateTime.UtcNow;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI request timed out after {Seconds}s (model {Model})", settings.TimeoutSeconds, settings.Model);
            throw new AiUnavailableException("Trợ lý AI phản hồi quá lâu, vui lòng thử lại.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("AI provider unreachable: {Error}", ex.Message);
            throw new AiUnavailableException("Không kết nối được dịch vụ AI.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Only the error code/type is logged - never the request (prompt) or the key.
                logger.LogWarning("AI provider returned {StatusCode} ({ErrorCode}) for model {Model}",
                    (int)response.StatusCode, await ReadErrorCodeAsync(response, cancellationToken), settings.Model);
                throw new AiUnavailableException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Khóa API của dịch vụ AI không hợp lệ hoặc đã hết hạn.",
                    HttpStatusCode.TooManyRequests => "Dịch vụ AI đang quá tải hoặc hết hạn mức, vui lòng thử lại sau.",
                    HttpStatusCode.NotFound => "Không tìm thấy model AI đã cấu hình.",
                    _ => "Dịch vụ AI đang gặp sự cố."
                });
            }

            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                var root = document.RootElement;
                var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new AiUnavailableException("Trợ lý AI trả về nội dung rỗng.");
                }

                int? promptTokens = null, completionTokens = null;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    promptTokens = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv) ? pv : null;
                    completionTokens = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : null;
                }

                var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : settings.Model;
                logger.LogInformation("AI completion by {Model} in {Ms} ms ({PromptTokens}+{CompletionTokens} tokens)",
                    model, (int)(DateTime.UtcNow - started).TotalMilliseconds, promptTokens, completionTokens);
                return new AiCompletionResult(content, model, promptTokens, completionTokens);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                logger.LogWarning("Unexpected AI response format: {Error}", ex.Message);
                throw new AiUnavailableException("Dịch vụ AI trả về dữ liệu không hợp lệ.", ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiUnavailableException("Trợ lý AI phản hồi quá lâu, vui lòng thử lại.");
            }
        }
    }

    private static Uri Endpoint(string baseUrl) => new(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), "chat/completions");

    private static string RoleName(AIMessageRole role) => role switch
    {
        AIMessageRole.System => "system",
        AIMessageRole.Assistant => "assistant",
        AIMessageRole.Tool => "tool",
        _ => "user"
    };

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
                var type = error.TryGetProperty("type", out var t) ? t.ToString() : null;
                return string.Join("/", new[] { code, type }.Where(v => !string.IsNullOrEmpty(v)));
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            // Body is not JSON: nothing useful to log.
        }

        return "unknown";
    }
}
