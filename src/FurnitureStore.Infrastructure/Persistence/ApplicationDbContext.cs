using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FurnitureStore.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        // Cascade deletes run at SaveChanges time, after AuditableEntityInterceptor has turned deletes of
        // soft-deletable entities (Product) into updates - so their variants/images are not physically removed.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
    }

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductColor> ProductColors => Set<ProductColor>();
    public DbSet<ProductMaterial> ProductMaterials => Set<ProductMaterial>();
    public DbSet<ProductSize> ProductSizes => Set<ProductSize>();
    public DbSet<ProductStyle> ProductStyles => Set<ProductStyle>();
    public DbSet<ProductVariantColor> ProductVariantColors => Set<ProductVariantColor>();
    public DbSet<ProductVariantMaterial> ProductVariantMaterials => Set<ProductVariantMaterial>();
    public DbSet<ProductVariantSize> ProductVariantSizes => Set<ProductVariantSize>();
    public DbSet<ProductPriceHistory> ProductPriceHistory => Set<ProductPriceHistory>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();

    // Sales
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

    // Customers
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();

    // Communication
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // AI
    public DbSet<AIConversation> AIConversations => Set<AIConversation>();
    public DbSet<AIMessage> AIMessages => Set<AIMessage>();
    public DbSet<AIKnowledgeEntry> AIKnowledgeEntries => Set<AIKnowledgeEntry>();
    public DbSet<QuoteRequest> QuoteRequests => Set<QuoteRequest>();

    // System
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StoreInfo> StoreInformation => Set<StoreInfo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Money: decimal(18,2). VND has no minor unit but 2 decimals keep room for percentages / other currencies.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // All timestamps are stored in UTC and read back with DateTimeKind.Utc.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums are stored as readable strings ("Pending", "COD"...) rather than magic numbers.
        foreach (var enumType in typeof(OrderStatus).Assembly.GetTypes().Where(t => t.IsEnum && t.Namespace == typeof(OrderStatus).Namespace))
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>().HaveMaxLength(40);
        }

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite (used by the test suite) cannot ORDER BY / aggregate decimals; store them as REAL there.
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
        }
    }
}
