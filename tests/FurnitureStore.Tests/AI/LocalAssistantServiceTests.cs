using FurnitureStore.Application.AI;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Admin;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.AI;

/// <summary>The assistant without an AI model, answering from the real (seeded) catalog, coupons and orders.</summary>
public sealed class LocalAssistantServiceTests : IAsyncLifetime
{
    private static readonly AiCaller Guest = new(null, "fedcba9876543210fedcba9876543210");
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private Task<AiChatResponse> AskAsync(string message, AiCaller? caller = null, int? conversationId = null, int? productId = null) =>
        Run(sp => sp.GetRequiredService<IAssistantService>().ChatAsync(caller ?? Guest,
            new AiChatRequest { Message = message, ConversationId = conversationId, ProductId = productId }));

    private Task<int> ProductIdAsync(string sku) => Db(db => db.Products.Where(p => p.Sku == sku).Select(p => p.Id).SingleAsync());

    [Fact]
    public async Task EverydayQuestions_GetStoreAnswers_WithoutAnyAiModel()
    {
        Assert.False(_host.Ai.IsConfigured);

        var hours = await AskAsync("Cửa hàng mở cửa mấy giờ vậy?");
        Assert.Contains("mở cửa", hours.Reply);
        Assert.Empty(hours.Products);

        var shipping = await AskAsync("phi ship bao nhieu");                 // typed without accents
        Assert.Contains("báo phí", shipping.Reply);                          // quoted when the store calls back
        Assert.Contains("Bảo hành bao lâu?", shipping.Suggestions);

        var thanks = await AskAsync("cảm ơn bạn");
        Assert.StartsWith("Không có gì", thanks.Reply);

        var unknown = await AskAsync("xyz qwerty");
        Assert.Contains("chưa hiểu", unknown.Reply);
        Assert.Empty(_host.Ai.Requests);
    }

    [Fact]
    public async Task Coupons_AreListedFromTheDatabase_EvenWhenAnAiModelIsConfigured()
    {
        _host.Ai.IsConfigured = true;
        _host.Ai.Respond = _ => new AiCompletionResult("{\"reply\":\"Dùng mã GIAM99 để giảm 99%\"}", "test-model", 10, 10);

        var reply = await AskAsync("Shop có mã giảm giá không?");

        Assert.Contains("CHAOBAN10", reply.Reply);
        Assert.Contains("GIAM500K", reply.Reply);
        Assert.DoesNotContain("HETHAN", reply.Reply);   // expired and private
        Assert.DoesNotContain("GIAM99", reply.Reply);   // nothing invented by a model
        Assert.Empty(_host.Ai.Requests);
    }

    [Fact]
    public async Task OrderStatus_ShowsTheSignedInCustomersOrders()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var owner = new CartOwner(userId, null);
        var variantId = await Db(db => db.ProductVariants.Where(v => v.Product.Sku == "GA-CURVE" && v.StockQuantity >= 2).Select(v => v.Id).FirstAsync());
        await Run(sp => sp.GetRequiredService<ICartService>().AddAsync(owner, variantId, 1));
        var placed = await Run(sp => sp.GetRequiredService<IOrderService>().PlaceOrderAsync(userId, new CheckoutCommand
        {
            FullName = "Khách Test", Phone = "0912345678", Email = "test@example.com", AddressLine = "12 Lê Lợi",
            Ward = "Phường Bến Nghé", Province = "TP. Hồ Chí Minh", PaymentMethod = PaymentMethod.COD
        }));

        var mine = await AskAsync("đơn hàng của tôi đến đâu rồi?", new AiCaller(userId, null));
        Assert.Contains(placed.OrderCode, mine.Reply);
        Assert.Contains("Chờ xác nhận", mine.Reply);

        var anonymous = await AskAsync("đơn hàng của tôi đến đâu rồi?");
        Assert.Contains("đăng nhập", anonymous.Reply);
        Assert.DoesNotContain(placed.OrderCode, anonymous.Reply);
    }

    [Fact]
    public async Task ProductPage_SuggestedDeliveryQuestion_IsAnswered_NotTheProductSummaryAgain()
    {
        // Regression: the product page suggests "Giao hàng mất bao lâu?", which used to repeat the product description.
        var sofaId = await ProductIdAsync("SF-OSLO");

        var reply = await AskAsync("Giao hàng mất bao lâu?", productId: sofaId);

        Assert.Contains("giao", reply.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thuộc nhóm", reply.Reply);
        Assert.Equal(sofaId, Assert.Single(reply.Products).ProductId);
    }

    [Fact]
    public async Task ProductPage_Warranty_UsesTheProductsOwnWarranty()
    {
        var product = await Db(db => db.Products.SingleAsync(p => p.Sku == "SF-OSLO"));

        var reply = await AskAsync("sản phẩm này bảo hành mấy tháng?", productId: product.Id);

        Assert.StartsWith($"{product.Name} được bảo hành {product.WarrantyMonths} tháng.", reply.Reply);
    }

    [Fact]
    public async Task ProductPage_CheaperQuestion_ShowsOnlyCheaperProductsOfTheSameCategory()
    {
        var milano = await Db(db => db.Products.SingleAsync(p => p.Sku == "SF-MILANO"));

        var reply = await AskAsync("Mẫu nào rẻ hơn?", productId: milano.Id);

        Assert.NotEmpty(reply.Products);
        Assert.All(reply.Products, p => Assert.True(p.Price < milano.BasePrice, $"{p.Name} {p.Price}"));
        Assert.All(reply.Products, p => Assert.Equal("Sofa", p.CategoryName));
    }

    [Fact]
    public async Task SmallTalk_AfterAProductSearch_DoesNotRepeatTheSearch()
    {
        var search = await AskAsync("bàn ăn 6 người khoảng 10 triệu");
        Assert.NotEmpty(search.Products);

        var thanks = await AskAsync("cảm ơn nhé", conversationId: search.ConversationId);

        Assert.StartsWith("Không có gì", thanks.Reply);
        Assert.Empty(thanks.Products);
    }

    [Fact]
    public async Task AfterASearch_UnrelatedMessagesAreNotUnderstood_ButFollowUpsContinueTheSearch()
    {
        var search = await AskAsync("Mình muốn mua bàn ăn cho 6 người");

        var gibberish = await AskAsync("xyz qwerty", conversationId: search.ConversationId);
        Assert.Contains("chưa hiểu", gibberish.Reply);
        Assert.Empty(gibberish.Products);

        var more = await AskAsync("còn mẫu nào khác không?", conversationId: search.ConversationId);
        Assert.NotEmpty(more.Products);
        Assert.Contains("bàn ăn", more.Reply);
    }

    [Fact]
    public async Task MaterialQuestion_NamingAProduct_ExplainsTheMaterial_AndShowsMatchingProducts()
    {
        var reply = await AskAsync("sofa da thật có bền không?");

        Assert.Contains("Da bò thật", reply.Reply);
        Assert.Contains("Ưu điểm", reply.Reply);
        Assert.NotEmpty(reply.Products);
    }

    [Fact]
    public async Task ProductSearch_StillWorks()
    {
        var reply = await AskAsync("sofa cho phòng khách dưới 20 triệu");

        Assert.NotEmpty(reply.Products);
        Assert.All(reply.Products, p => Assert.True(p.Price <= 23_000_000));
    }
}
