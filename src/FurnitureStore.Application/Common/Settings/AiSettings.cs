using System.ComponentModel.DataAnnotations;

namespace FurnitureStore.Application.Common.Settings;

/// <summary>
/// AI provider configuration, bound from the "AI" section.
/// ApiKey must NOT be stored in appsettings.json: use `dotnet user-secrets set "AI:ApiKey" "..."`
/// in development or the environment variable AI__ApiKey on servers.
/// </summary>
public sealed class AiSettings
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; } = true;

    /// <summary>"OpenAI" or any OpenAI-compatible Chat Completions API.</summary>
    [Required]
    public string Provider { get; set; } = "OpenAI";

    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    [Required]
    public string Model { get; set; } = "gpt-4o-mini";

    public string? ApiKey { get; set; }

    [Range(64, 8192)]
    public int MaxOutputTokens { get; set; } = 800;

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.3;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Per-user (or per-IP for guests) request limit for /api/ai/* endpoints.</summary>
    [Range(1, 1000)]
    public int RequestsPerMinute { get; set; } = 10;

    /// <summary>Ask for a JSON object answer (response_format). Turn off for providers that do not support it.</summary>
    public bool JsonMode { get; set; } = true;

    /// <summary>"max_tokens" (most OpenAI-compatible APIs) or "max_completion_tokens" (newer OpenAI reasoning models).</summary>
    [RegularExpression("^(max_tokens|max_completion_tokens)$")]
    public string TokenLimitParameter { get; set; } = "max_tokens";

    /// <summary>Some models only accept the default temperature; set false to omit it.</summary>
    public bool SendTemperature { get; set; } = true;

    /// <summary>
    /// Sent as "reasoning_effort" when set. Thinking models count their reasoning in the output tokens: Gemini 2.5 Flash
    /// accepts "none" (no thinking, the whole budget goes to the answer); Gemini 3 models cannot turn it off - use "minimal"
    /// or "low". Empty: not sent (OpenAI gpt-4o-mini and most compatible APIs).
    /// </summary>
    [RegularExpression("^(none|minimal|low|medium|high)$")]
    public string? ReasoningEffort { get; set; }

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
