using FurnitureStore.Application;
using FurnitureStore.Application.Admin;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Application.Sales;
using FurnitureStore.Infrastructure.Payments;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Interceptors;
using FurnitureStore.Infrastructure.Persistence.Repositories;
using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FurnitureStore.Tests.Infrastructure;

/// <summary>
/// Real application services + EF repositories over an in-memory SQLite database, without the web host.
/// Optionally seeded with the demo catalog.
/// </summary>
public sealed class ServiceTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    private ServiceTestHost(SqliteConnection connection, ServiceProvider provider, FixedTimeProvider clock)
    {
        _connection = connection;
        _provider = provider;
        Clock = clock;
    }

    public FixedTimeProvider Clock { get; }
    public InMemoryFileStorage Files => _provider.GetRequiredService<InMemoryFileStorage>();
    public CapturingEmailSender Emails => _provider.GetRequiredService<CapturingEmailSender>();
    public CapturingChatNotifier ChatEvents => _provider.GetRequiredService<CapturingChatNotifier>();
    public FakeAiChatClient Ai => _provider.GetRequiredService<FakeAiChatClient>();

    public static async Task<ServiceTestHost> CreateAsync(bool seedCatalog = true, ICurrentUserService? currentUser = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var clock = FixedTimeProvider.At(2026, 9, 1);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplication();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(currentUser ?? new TestCurrentUser("admin@furniture.local", "admin-id", "ADMIN"));
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseSqlite(connection).AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>())
                .ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning)));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogAdminRepository, CatalogAdminRepository>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<InMemoryFileStorage>();
        services.AddSingleton<IFileStorageService>(sp => sp.GetRequiredService<InMemoryFileStorage>());
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IPaymentProvider, CodPaymentProvider>();
        services.AddScoped<IPaymentProvider, BankTransferPaymentProvider>();
        services.AddSingleton<CapturingEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
        services.AddScoped<IAdminReportRepository, AdminReportRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddSingleton<CapturingChatNotifier>();
        services.AddSingleton<IChatNotifier>(sp => sp.GetRequiredService<CapturingChatNotifier>());
        services.AddScoped<IProductFactRepository, ProductFactRepository>();
        services.AddScoped<IAiKnowledgeRepository, AiKnowledgeRepository>();
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IPriceRuleRepository, PriceRuleRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddSingleton<FakeAiChatClient>();
        services.AddSingleton<IAiChatClient>(sp => sp.GetRequiredService<FakeAiChatClient>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = new ServiceTestHost(connection, provider, clock);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        if (seedCatalog)
        {
            await new CatalogSeeder(context, clock, NullLogger<CatalogSeeder>.Instance).SeedAsync();
            await new SalesSeeder(context, clock, NullLogger<SalesSeeder>.Instance).SeedAsync();
            await new PriceRuleSeeder(context, NullLogger<PriceRuleSeeder>.Instance).SeedAsync();
        }

        return host;
    }

    /// <summary>Runs <paramref name="action"/> with services from a fresh scope (like one HTTP request).</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task RunAsync(Func<IServiceProvider, Task> action) =>
        RunAsync<object?>(async sp => { await action(sp); return null; });

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>AI provider double: not configured by default (rule-based assistant); tests can script its answers.</summary>
public sealed class FakeAiChatClient : IAiChatClient
{
    public bool IsConfigured { get; set; }
    public Func<AiCompletionRequest, AiCompletionResult>? Respond { get; set; }
    public List<AiCompletionRequest> Requests { get; } = [];

    public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult((Respond ?? throw new InvalidOperationException("No scripted AI answer."))(request));
    }
}

/// <summary>Records realtime chat events instead of pushing them through SignalR.</summary>
public sealed class CapturingChatNotifier : IChatNotifier
{
    public List<(ChatCustomer Customer, ChatConversationDto Conversation, ChatMessageDto Message)> Sent { get; } = [];
    public List<(ChatCustomer Customer, int ConversationId, FurnitureStore.Domain.Enums.ChatSenderType Reader)> Reads { get; } = [];

    public Task MessageSentAsync(ChatCustomer customer, ChatConversationDto conversation, ChatMessageDto message, CancellationToken cancellationToken = default)
    {
        lock (Sent) Sent.Add((customer, conversation, message));
        return Task.CompletedTask;
    }

    public Task MessagesReadAsync(ChatCustomer customer, ChatConversationDto conversation, FurnitureStore.Domain.Enums.ChatSenderType reader, CancellationToken cancellationToken = default)
    {
        lock (Reads) Reads.Add((customer, conversation.Id, reader));
        return Task.CompletedTask;
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (Sent)
        {
            Sent.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>File storage double that validates images like the real one but keeps them in memory.</summary>
public sealed class InMemoryFileStorage : IFileStorageService
{
    private int _counter;
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task<StoredFile> SaveImageAsync(Stream content, string originalFileName, string folder, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var errors = ImageFileValidator.Validate(originalFileName, bytes.Length, bytes.AsSpan(0, Math.Min(bytes.Length, 12)),
            [".jpg", ".jpeg", ".png", ".webp"], 5 * 1024 * 1024);
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        var url = $"/uploads/{folder}/test-{Interlocked.Increment(ref _counter)}{Path.GetExtension(originalFileName).ToLowerInvariant()}";
        Files[url] = bytes;
        return new StoredFile(url, Path.GetFileName(url), bytes.Length, "image/png");
    }

    public Task DeleteAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (url is not null)
        {
            Files.Remove(url);
        }

        return Task.CompletedTask;
    }

    public static MemoryStream TinyPng() =>
        new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52]);
}
