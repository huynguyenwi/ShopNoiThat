using System.Collections.Concurrent;
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
/// Chat Completions client for OpenAI and OpenAI-compatible APIs (Google Gemini's OpenAI endpoint, Azure OpenAI proxy, Groq,
/// OpenRouter, Ollama, LM Studio...). The API key comes from configuration (user-secrets / environment variable) and is never logged.
/// </summary>
public sealed class OpenAiCompatibleChatClient(HttpClient http, IOptionsMonitor<AiSettings> options, ILogger<OpenAiCompatibleChatClient> logger) : IAiChatClient
{
    /// <summary>Endpoint + model pairs that refused response_format: asked without it from then on (until restart).</summary>
    private static readonly ConcurrentDictionary<string, bool> JsonModeRejected = new();

    /// <summary>Wait before the single retry of a transient provider error (tests shorten it).</summary>
    internal static TimeSpan TransientRetryDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Overloaded or failing for a moment (429 means a quota: retrying right away would not help).</summary>
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new AiUnavailableException("Trợ lý AI chưa được cấu hình.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var started = DateTime.UtcNow;
        var providerKey = $"{settings.BaseUrl}|{settings.Model}";
        var jsonMode = request.JsonResponse && settings.JsonMode && !JsonModeRejected.ContainsKey(providerKey);
        var response = await SendAsync(settings, request, jsonMode, cancellationToken, timeout.Token);

        // A busy provider (Gemini free tier: 503 "high demand") usually answers a second later: one retry before falling back.
        if (IsTransient(response.StatusCode))
        {
            logger.LogWarning("AI provider returned {StatusCode} for model {Model}; retrying once", (int)response.StatusCode, settings.Model);
            response.Dispose();
            try
            {
                await Task.Delay(TransientRetryDelay, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiUnavailableException("Trợ lý AI phản hồi quá lâu, vui lòng thử lại.");
            }

            response = await SendAsync(settings, request, jsonMode, cancellationToken, timeout.Token);
        }

        // JSON mode is optional: a provider that does not know response_format gets the same request without it
        // (the prompts already ask for JSON, and ModelJson reads an object out of a fenced or wrapped answer).
        ProviderError? error = null;
        if (jsonMode && response.StatusCode == HttpStatusCode.BadRequest)
        {
            error = await ReadErrorAsync(response, cancellationToken);
            if (!error.IsApiKeyError) // Gemini answers a wrong key with 400 too: retrying would not help
            {
                logger.LogWarning("AI provider refused the request with JSON mode ({ErrorCode}) for model {Model}; retrying without response_format",
                    error.Code, settings.Model);
                response.Dispose();
                error = null;
                response = await SendAsync(settings, request, jsonMode: false, cancellationToken, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    JsonModeRejected[providerKey] = true;
                }
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                error ??= await ReadErrorAsync(response, cancellationToken);
                // Only the error code/status is logged - never the request (prompt), the provider's message or the key.
                logger.LogWarning("AI provider returned {StatusCode} ({ErrorCode}) for model {Model}",
                    (int)response.StatusCode, error.Code, settings.Model);
                throw new AiUnavailableException(response.StatusCode switch
                {
                    HttpStatusCode.BadRequest when error.IsApiKeyError => "Khóa API của dịch vụ AI không hợp lệ hoặc đã hết hạn.",
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
                var choice = root.GetProperty("choices")[0];
                var content = choice.GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                    // "length" with nothing written: a thinking model spent the whole token budget reasoning.
                    logger.LogWarning("AI answer from {Model} is empty (finish_reason {FinishReason}); if 'length', raise AI:MaxOutputTokens or lower AI:ReasoningEffort",
                        settings.Model, finish ?? "none");
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

    private async Task<HttpResponseMessage> SendAsync(AiSettings settings, AiCompletionRequest request, bool jsonMode,
        CancellationToken cancellationToken, CancellationToken timeoutToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["messages"] = request.Messages.Select(m => new { role = RoleName(m.Role), content = m.Content }).ToList(),
            [settings.TokenLimitParameter] = settings.MaxOutputTokens
        };
        if (settings.SendTemperature) body["temperature"] = settings.Temperature;
        if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort)) body["reasoning_effort"] = settings.ReasoningEffort;
        if (jsonMode) body["response_format"] = new { type = "json_object" };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.BaseUrl)) { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        try
        {
            return await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutToken);
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
    }

    private static Uri Endpoint(string baseUrl) => new(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), "chat/completions");

    private static string RoleName(AIMessageRole role) => role switch
    {
        AIMessageRole.System => "system",
        AIMessageRole.Assistant => "assistant",
        AIMessageRole.Tool => "tool",
        _ => "user"
    };

    /// <param name="Code">Code and type / status of the error, safe to log (the provider's message may echo the key).</param>
    /// <param name="IsApiKeyError">The message is about the API key (Gemini: 400 "Please pass a valid API key").</param>
    private sealed record ProviderError(string Code, bool IsApiKeyError);

    /// <summary>Reads {"error": {...}} (OpenAI) or [{"error": {...}}] (Gemini's OpenAI endpoint).</summary>
    private static async Task<ProviderError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
                var type = error.TryGetProperty("type", out var t) ? t.ToString()
                    : error.TryGetProperty("status", out var s) ? s.ToString() : null;
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var keyError = message is not null
                               && (message.Contains("API key", StringComparison.OrdinalIgnoreCase) || message.Contains("api_key", StringComparison.OrdinalIgnoreCase));
                return new ProviderError(string.Join("/", new[] { code, type }.Where(v => !string.IsNullOrEmpty(v))), keyError);
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            // Body is not JSON: nothing useful to log.
        }

        return new ProviderError("unknown", false);
    }
}
