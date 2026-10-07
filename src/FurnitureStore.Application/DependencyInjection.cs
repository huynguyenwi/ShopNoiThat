using FluentValidation;
using FurnitureStore.Application.Admin;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Qr;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Sales;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application-layer services (business logic). Infrastructure implementations
    /// of the interfaces declared here are registered by AddInfrastructure().
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Catalog
        services.AddSingleton<CatalogCache>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProductAdminService, ProductAdminService>();
        services.AddScoped<ICategoryAdminService, CategoryAdminService>();
        services.AddScoped<IAttributeAdminService, AttributeAdminService>();

        // Sales
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<OrderWorkflow>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICouponAdminService, CouponAdminService>();
        services.AddScoped<IQrLookupService, QrLookupService>();

        // Admin & engagement
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOrderAdminService, OrderAdminService>();
        services.AddScoped<IAdminActivityService, AdminActivityService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<IStoreInfoService, StoreInfoService>();
        services.AddScoped<IHomeBannerService, HomeBannerService>();

        services.AddScoped<IChatService, ChatService>();

        services.AddScoped<ProductMatcher>();
        services.AddScoped<IAssistantService, AssistantService>();
        services.AddScoped<IAiAdminService, AiAdminService>();

        services.AddScoped<PriceCalculatorService>();
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<IPriceRuleAdminService, PriceRuleAdminService>();

        return services;
    }
}
