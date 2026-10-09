using System.Net;
using System.Text;
using System.Text.Json;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Tests.AI;

public sealed class OpenAiCompatibleChatClientTests
{
    private const string Key = "sk-test-SECRET-123";

    public OpenAiCompatibleChatClientTests() => OpenAiCompatibleChatClient.TransientRetryDelay = TimeSpan.FromMilliseconds(10);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(Body ?? string.Empty);
            return await respond(request, cancellationToken);
        }
    }

    private sealed class ListLogger : ILogger<OpenAiCompatibleChatClient>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class Monitor(AiSettings value) : IOptionsMonitor<AiSettings>
    {
        public AiSettings CurrentValue => value;
        public AiSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<AiSettings, string?> listener) => null;
    }

    private static (OpenAiCompatibleChatClient Client, StubHandler Handler, ListLogger Log) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, Action<AiSettings>? configure = null)
    {
        var settings = new AiSettings { ApiKey = Key, BaseUrl = "https://ai.example.com/v1", Model = "test-model", TimeoutSeconds = 5 };
        configure?.Invoke(settings);
        var handler = new StubHandler(respond);
        var log = new ListLogger();
        return (new OpenAiCompatibleChatClient(new HttpClient(handler), new Monitor(settings), log), handler, log);
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private static readonly AiCompletionRequest Request = new([
        new AiChatMessage(AIMessageRole.System, "Quy tắc"),
        new AiChatMessage(AIMessageRole.User, "Xin chào")
    ]);

    [Fact]
    public async Task SendsAnOpenAiChatCompletionRequest_AndReadsTheAnswer()
    {
        var (client, handler, log) = Create((_, _) => Json(HttpStatusCode.OK,
            """{"model":"test-model-2026","choices":[{"message":{"role":"assistant","content":"{\"reply\":\"Chào bạn\"}"}}],"usage":{"prompt_tokens":11,"completion_tokens":7}}"""));

        var result = await client.CompleteAsync(Request);

        Assert.Equal("""{"reply":"Chào bạn"}""", result.Content);
        Assert.Equal("test-model-2026", result.Model);
        Assert.Equal(11, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://ai.example.com/v1/chat/completions", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(Key, handler.Request.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Xin chào", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(800, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _)); // not sent unless configured

        Assert.DoesNotContain(log.Lines, line => line.Contains(Key));
    }

    [Fact]
    public async Task GeminiSettings_SendReasoningEffort_ToTheOpenAiCompatibleEndpoint()
    {
        var (client, handler, _) = Create((_, _) => Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}"""), s =>
        {
            s.BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/";
            s.Model = "gemini-2.5-flash";
            s.ReasoningEffort = "none";
        });

        await client.CompleteAsync(Request);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", handler.Request!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("none", body.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("gemini-2.5-flash", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task JsonModeRefused_IsRetriedWithoutIt_AndNotSentAgain()
    {
        var (client, handler, log) = Create((request, _) =>
        {
            var json = request.Content!.ReadAsStringAsync().Result;
            return json.Contains("response_format")
                ? Json(HttpStatusCode.BadRequest, """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"Unknown name response_format"}}""")
                : Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"```json\n{\"reply\":\"Chào\"}\n```"}}]}""");
        }, s => s.Model = "json-refusing-model");

        var first = await client.CompleteAsync(Request with { JsonResponse = true });
        var second = await client.CompleteAsync(Request with { JsonResponse = true });

        Assert.Contains("\"reply\"", first.Content);
        Assert.Contains("\"reply\"", second.Content);
        Assert.Equal(3, handler.Bodies.Count);                      // refused, retried, then asked without JSON mode right away
        Assert.Contains("response_format", handler.Bodies[0]);
        Assert.DoesNotContain("response_format", handler.Bodies[1]);
        Assert.DoesNotContain("response_format", handler.Bodies[2]);
        Assert.Contains(log.Lines, line => line.Contains("retrying without response_format"));
    }

    [Fact]
    public async Task GeminiWrongKey_Is400_ReportedAsAKeyProblem_WithoutRetrying()
    {
        // The exact answer of generativelanguage.googleapis.com/v1beta/openai/chat/completions to an invalid key.
        var (client, handler, log) = Create((_, _) => Json(HttpStatusCode.BadRequest,
            """[{"error":{"code":400,"message":"Please pass a valid API key","status":"INVALID_ARGUMENT"}}]"""), s => s.Model = "gemini-wrong-key");

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request with { JsonResponse = true }));

        Assert.Contains("không hợp lệ", ex.Message);
        Assert.Single(handler.Bodies);                                  // not mistaken for "JSON mode not supported"
        Assert.Contains(log.Lines, line => line.Contains("400/INVALID_ARGUMENT"));
        Assert.DoesNotContain(log.Lines, line => line.Contains("Please pass a valid API key"));
    }

    [Fact]
    public async Task BusyProvider_IsRetriedOnce_AndTheSecondAnswerIsUsed()
    {
        var calls = 0;
        var (client, handler, log) = Create((_, _) => ++calls == 1
            ? Json(HttpStatusCode.ServiceUnavailable, """[{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}]""")
            : Json(HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop","message":{"content":"{\"reply\":\"Chào bạn\"}"}}]}"""), s => s.Model = "busy-model");

        var result = await client.CompleteAsync(Request);

        Assert.Equal("""{"reply":"Chào bạn"}""", result.Content);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains(log.Lines, line => line.Contains("503") && line.Contains("retrying once"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2)] // still busy after the retry: falls back
    [InlineData(HttpStatusCode.TooManyRequests, 1)]    // quota: not retried
    public async Task Outages_AreRetriedAtMostOnce(HttpStatusCode status, int expectedRequests)
    {
        var (client, handler, _) = Create((_, _) => Json(status, """{"error":{"code":"busy"}}"""), s => s.Model = $"outage-{(int)status}");

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));

        Assert.Equal(expectedRequests, handler.Bodies.Count);
    }

    [Fact]
    public async Task OtherBadRequests_AreNotRetried()
    {
        var (client, handler, _) = Create((_, _) => Json(HttpStatusCode.BadRequest, """{"error":{"code":"invalid_model"}}"""),
            s => { s.Model = "no-json-model"; s.JsonMode = false; });

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request with { JsonResponse = true }));

        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task EmptyAnswerCutByTheTokenLimit_IsExplainedInTheLog()
    {
        var (client, _, log) = Create((_, _) => Json(HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"length","message":{"role":"assistant","content":null}}]}"""), s => s.Model = "thinking-model");

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));

        Assert.Contains(log.Lines, line => line.Contains("finish_reason length") && line.Contains("AI:MaxOutputTokens"));
    }

    [Fact]
    public async Task CompatibilitySwitches_ChangeTheRequest()
    {
        var (client, handler, _) = Create((_, _) => Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}"""), s =>
        {
            s.JsonMode = false;
            s.TokenLimitParameter = "max_completion_tokens";
            s.SendTemperature = false;
        });

        await client.CompleteAsync(Request);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
        Assert.Equal(800, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "không hợp lệ")]
    [InlineData(HttpStatusCode.TooManyRequests, "quá tải")]
    [InlineData(HttpStatusCode.InternalServerError, "sự cố")]
    public async Task ProviderErrors_BecomeAiUnavailable_WithoutLeakingTheKey(HttpStatusCode status, string expected)
    {
        var (client, _, log) = Create((_, _) => Json(status, $$$"""{"error":{"message":"Incorrect API key provided: {{{Key}}}","type":"invalid_request_error","code":"invalid_api_key"}}"""));

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
        Assert.Contains(log.Lines, line => line.Contains(((int)status).ToString()));
        Assert.DoesNotContain(log.Lines, line => line.Contains(Key)); // the provider's error message (which echoes the key) is not logged
    }

    [Fact]
    public async Task Timeout_BecomesAiUnavailable()
    {
        var (client, _, _) = Create(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, s => s.TimeoutSeconds = 5);
        var started = DateTime.UtcNow;

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));

        Assert.Contains("quá lâu", ex.Message);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    [InlineData("not json")]
    public async Task MalformedAnswers_BecomeAiUnavailable(string json)
    {
        var (client, _, _) = Create((_, _) => Json(HttpStatusCode.OK, json));

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));
    }

    [Fact]
    public async Task WithoutApiKey_NothingIsSent()
    {
        var (client, handler, _) = Create((_, _) => Json(HttpStatusCode.OK, "{}"), s => s.ApiKey = "");

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.CompleteAsync(Request));
        Assert.Null(handler.Request);
    }
}
