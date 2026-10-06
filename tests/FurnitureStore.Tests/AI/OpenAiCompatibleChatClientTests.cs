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

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
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

        Assert.DoesNotContain(log.Lines, line => line.Contains(Key));
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
