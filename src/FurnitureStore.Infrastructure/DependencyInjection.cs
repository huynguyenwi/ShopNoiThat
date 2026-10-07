using FurnitureStore.Application.Admin;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Qr;
using FurnitureStore.Application.Sales;
using FurnitureStore.Infrastructure.AI;
using FurnitureStore.Infrastructure.Email;
using FurnitureStore.Infrastructure.Payments;
using FurnitureStore.Infrastructure.Qr;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Services;
using FurnitureStore.Infrastructure.Storage;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Interceptors;
using FurnitureStore.Infrastructure.Persistence.Repositories;
using FurnitureStore.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure concerns: configuration-bound options, persistence,
    /// identity, AI clients, payment providers and file storage.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddSettings(services, configuration);
        AddPersistence(services, configuration);
        AddIdentityCore(services);
        AddServices(services, configuration);

        return services;
    }

    private static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddScoped<IPaymentProvider, CodPaymentProvider>();
        services.AddScoped<IPaymentProvider, BankTransferPaymentProvider>();

        // AI provider (OpenAI-compatible). The client enforces AI:TimeoutSeconds itself.
        services.AddHttpClient<IAiChatClient, OpenAiCompatibleChatClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        var emailMode = configuration.GetSection(EmailSettings.SectionName)[nameof(EmailSettings.Mode)];
        if (string.Equals(emailMode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, PickupDirectoryEmailSender>();
        }
    }

    private static void AddSettings(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageSettings>()
            .Bind(configuration.GetSection(StorageSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PaymentSettings>()
            .Bind(configuration.GetSection(PaymentSettings.SectionName));

        services.AddOptions<AiSettings>()
            .Bind(configuration.GetSection(AiSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection(DatabaseSettings.SectionName));

        services.AddOptions<SeedSettings>()
            .Bind(configuration.GetSection(SeedSettings.SectionName));

        services.AddOptions<EmailSettings>()
            .Bind(configuration.GetSection(EmailSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing. Configure it in appsettings.json, user-secrets or the " +
                "environment variable ConnectionStrings__DefaultConnection.");
        }

        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            });
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogAdminRepository, CatalogAdminRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IAdminReportRepository, AdminReportRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IProductFactRepository, ProductFactRepository>();
        services.AddScoped<IAiKnowledgeRepository, AiKnowledgeRepository>();
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<IPriceRuleRepository, PriceRuleRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        services.AddScoped<IdentitySeeder>();
        services.AddScoped<CatalogSeeder>();
        services.AddScoped<SalesSeeder>();
        services.AddScoped<DemoActivitySeeder>();
        services.AddScoped<AiKnowledgeSeeder>();
        services.AddScoped<PriceRuleSeeder>();
        services.AddScoped<DatabaseInitializer>();

        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = AccountSecurity.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = AccountSecurity.LockoutDuration;

                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationSignInManager>()
            .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>()
            .AddErrorDescriber<VietnameseIdentityErrorDescriber>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = AccountSecurity.PasswordResetTokenLifespan);

        // Re-validate the cookie against the database every minute, so locking an account or changing the
        // password signs the user out everywhere quickly.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));
    }
}
