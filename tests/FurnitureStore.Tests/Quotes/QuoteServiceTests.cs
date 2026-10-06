using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Admin;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Quotes;

public sealed class QuoteServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Quotes<T>(Func<IQuoteService, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IQuoteService>()));
    private Task Quotes(Func<IQuoteService, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IQuoteService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private async Task<ParsedQuoteRequest> ParseAsync(string text)
    {
        var options = await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetFilterOptionsAsync());
        return QuoteRequestParser.Parse(text, new CatalogVocabulary(options), options);
    }

    private Task<int> MaterialIdAsync(string slug) => Db(db => db.ProductMaterials.Where(m => m.Slug == slug).Select(m => m.Id).SingleAsync());

    private static QuoteSubmitCommand Submit(string text) => new() { Text = text, Name = "Anh Khoa", Phone = "0912345678", Email = "khoa@example.com", Note = "Giao quận 7" };

    // ------------------------------------------------------------------ parsing

    [Fact]
    public async Task Parser_ReadsTheSpecificationExample()
    {
        var parsed = await ParseAsync("Tôi muốn bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm.");

        Assert.Equal("ban-an", parsed.Kind);
        Assert.Equal((2200, 1000, 750), (parsed.LengthMm, parsed.WidthMm, parsed.HeightMm));
        Assert.Equal(await MaterialIdAsync("go-oc-cho"), parsed.MaterialId);
        Assert.True(parsed.MaterialIsExplicit);
        Assert.Null(parsed.Quantity);
    }

    [Theory]
    [InlineData("giường gỗ sồi 1m6", "giuong-ngu", 2000, 1600, null, null, null)]
    [InlineData("tủ quần áo cao 2m, sơn 2K, 2 cái", "tu-quan-ao", null, null, 2000, FinishType.TwoK, 2)]
    [InlineData("sofa vải nhung dài 2m4", "sofa", 2400, null, null, null, null)]
    [InlineData("bàn trà cao 45cm lau dầu", "ban-tra", null, null, 450, FinishType.NaturalOil, null)]
    [InlineData("ban go oc cho dai 1m8", "ban-an", 1800, null, null, null, null)]
    [InlineData("kệ sách 80x30x180, số lượng 3", "ke-sach", 800, 300, 1800, null, 3)]
    public async Task Parser_KindsSizesFinishAndQuantity(string text, string kind, int? length, int? width, int? height, FinishType? finish, int? quantity)
    {
        var parsed = await ParseAsync(text);

        Assert.Equal(kind, parsed.Kind);
        if (length.HasValue) Assert.Equal(length, parsed.LengthMm);
        if (width.HasValue) Assert.Equal(width, parsed.WidthMm);
        if (height.HasValue) Assert.Equal(height, parsed.HeightMm);
        Assert.Equal(finish, parsed.Finish);
        Assert.Equal(quantity, parsed.Quantity);
    }

    [Fact]
    public async Task Parser_GenericWood_IsNotExplicit()
    {
        var parsed = await ParseAsync("kệ sách gỗ tự nhiên");

        Assert.Equal(await MaterialIdAsync("go-soi"), parsed.MaterialId);
        Assert.False(parsed.MaterialIsExplicit);
    }

    // ------------------------------------------------------------------ estimate

    [Fact]
    public async Task Estimate_FromText_UsesTheCalculator_ListsAssumptions_AlternativesAndCatalogReferences()
    {
        var estimate = await Quotes(q => q.EstimateAsync(new QuoteEstimateRequest { Text = "Tôi muốn bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm" }, true));

        Assert.False(estimate.AiEnabled);
        Assert.Equal("Bàn ăn", estimate.Spec.TypeName);
        Assert.Equal((2200, 1000, 750), (estimate.Spec.LengthMm, estimate.Spec.WidthMm, estimate.Spec.HeightMm));
        Assert.Equal("Gỗ óc chó", estimate.Spec.MaterialName);
        Assert.Contains(estimate.Assumptions, a => a.Contains("kiểu sơn"));          // finish not given → PU assumed and said
        Assert.DoesNotContain(estimate.Assumptions, a => a.Contains("chiều"));       // all sizes were given

        var rules = await _host.RunAsync(sp => sp.GetRequiredService<IPriceRuleRepository>().GetActiveAsync());
        var expected = PriceCalculatorService.Calculate(new QuoteSpec(FurnitureType.Table, 2200, 1000, 750, estimate.Spec.MaterialId, FinishType.PU, 1), rules, _host.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(expected.UnitPrice, estimate.Breakdown.UnitPrice);
        Assert.Contains(AiPrompts.Vnd(expected.UnitPrice), estimate.Explanation);

        Assert.NotEmpty(estimate.Alternatives);                                        // other woods, cheapest first
        Assert.True(estimate.Alternatives.SequenceEqual(estimate.Alternatives.OrderBy(a => a.UnitPrice)));
        Assert.All(estimate.Alternatives, a => Assert.True(a.UnitPrice < estimate.Breakdown.UnitPrice)); // walnut is the most expensive wood
        Assert.All(estimate.SimilarProducts, p => Assert.Equal("Bàn ăn", p.CategoryName));
    }

    [Fact]
    public async Task Estimate_StructuredFieldsWin_AndMissingSizesUseTypicalValues()
    {
        var estimate = await Quotes(q => q.EstimateAsync(new QuoteEstimateRequest { Text = "bàn ăn dài 2m", Kind = "ban-an", LengthMm = 1600 }, false));

        Assert.Equal(1600, estimate.Spec.LengthMm);
        Assert.Equal(850, estimate.Spec.WidthMm); // typical dining table depth
        Assert.Contains(estimate.Assumptions, a => a.Contains("chiều rộng"));
        Assert.Contains(estimate.Assumptions, a => a.Contains("chất liệu"));
    }

    [Fact]
    public async Task Estimate_WithoutAKind_AsksForIt()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Quotes(q => q.EstimateAsync(new QuoteEstimateRequest { Text = "làm cho mình cái gì đó đẹp đẹp" }, false)));

        Assert.True(ex.FieldErrors.ContainsKey("Kind"));
    }

    [Fact]
    public async Task Estimate_Ai_FillsGapsOnly_AndItsNumbersAreChecked()
    {
        _host.Ai.IsConfigured = true;
        _host.Ai.Respond = request => request.Messages[0].Content.StartsWith("Bạn trích xuất")
            ? new AiCompletionResult("""{"kind":"ke-sach","lengthMm":900,"widthMm":350,"heightMm":1500,"material":"go-soi","finish":"PU","quantity":1}""", "m", 10, 10)
            : new AiCompletionResult("""{"explanation":"Giá chỉ 999.000đ thôi, rất rẻ!"}""", "m", 10, 10);

        var estimate = await Quotes(q => q.EstimateAsync(new QuoteEstimateRequest { Text = "Mình cần đóng một món để đựng sách, cao khoảng 2m" }, true));

        Assert.True(estimate.AiEnabled);
        Assert.Equal("ke-sach", estimate.Spec.Kind);      // from the model (the rules did not recognise it)
        Assert.Equal(2000, estimate.Spec.HeightMm);         // the number read by the rules wins over the model's 1500
        Assert.Equal(350, estimate.Spec.WidthMm);           // gap filled by the model
        Assert.DoesNotContain("999.000đ", estimate.Explanation);
        Assert.Contains(PriceGuard.Replacement, estimate.Explanation);
    }

    [Fact]
    public async Task Estimate_AiReturningAnUnknownKind_IsIgnored()
    {
        _host.Ai.IsConfigured = true;
        _host.Ai.Respond = _ => new AiCompletionResult("""{"kind":"tu-lanh"}""", "m", 1, 1);

        await Assert.ThrowsAsync<AppValidationException>(() => Quotes(q => q.EstimateAsync(new QuoteEstimateRequest { Text = "đóng cho mình một chiếc xích đu gỗ" }, false)));
    }

    // ------------------------------------------------------------------ submit & workflow

    [Fact]
    public async Task Submit_SavesARecomputedQuote_NotifiesAdmins_AndEmailsTheCustomer()
    {
        var command = Submit("bàn gỗ óc chó dài 1m8 rộng 90cm cao 75cm sơn PU, 2 cái");

        var submitted = await Quotes(q => q.SubmitAsync(command, null));

        Assert.Matches("^BG\\d{6}-[A-Z2-9]{5}$", submitted.Code);
        var quote = await Db(db => db.QuoteRequests.AsNoTracking().SingleAsync(q => q.Id == submitted.Id));
        Assert.Equal(QuoteStatus.New, quote.Status);
        Assert.Equal(2, quote.Quantity);
        Assert.Equal(17_800_000m, quote.EstimatedUnitPrice);   // same as the hand-checked calculation
        Assert.Equal(35_600_000m, quote.EstimatedTotal);
        Assert.Null(quote.FinalQuotedPrice);
        Assert.Null(quote.UserId);
        Assert.Equal("Giao quận 7", quote.CustomerNote);
        Assert.True(await Db(db => db.Notifications.AnyAsync(n => n.RecipientRole == AppRoles.Admin && n.Type == NotificationType.NewQuoteRequest)));
        Assert.Contains(_host.Emails.Sent, e => e.To == "khoa@example.com" && e.Subject.Contains(submitted.Code));
    }

    [Fact]
    public async Task Submit_Validation()
    {
        var command = Submit("bàn ăn gỗ sồi");
        command.Phone = "123";
        command.Name = "";

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Quotes(q => q.SubmitAsync(command, null)));

        Assert.True(ex.FieldErrors.ContainsKey("Phone"));
        Assert.True(ex.FieldErrors.ContainsKey("Name"));
    }

    [Fact]
    public async Task Workflow_AdminQuotes_CustomerAccepts()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var submitted = await Quotes(q => q.SubmitAsync(Submit("tủ quần áo gỗ sồi 1m6 cao 2m"), userId));
        var detail = await Quotes(q => q.GetAsync(submitted.Id));
        Assert.Contains(QuoteStatus.Quoted, detail.NextStatuses);

        // Quoting without a price is not allowed.
        await Assert.ThrowsAsync<DomainException>(() => Quotes(q => q.UpdateAsync(submitted.Id,
            new AdminQuoteUpdateCommand { Status = QuoteStatus.Quoted, Version = detail.Version }, "admin")));

        await Quotes(q => q.UpdateAsync(submitted.Id, new AdminQuoteUpdateCommand
        {
            Status = QuoteStatus.Quoted, FinalQuotedPrice = 21_500_000, AdminNote = "Đã gồm lắp đặt", Version = detail.Version
        }, "admin@furniture.local"));

        var quoted = await Quotes(q => q.GetMineAsync(userId, submitted.Code.ToLowerInvariant()));
        Assert.Equal(QuoteStatus.Quoted, quoted.Status);
        Assert.Equal(21_500_000m, quoted.FinalQuotedPrice);
        Assert.Null(quoted.AdminNote);   // internal note hidden from the customer
        Assert.True(await Db(db => db.Notifications.AnyAsync(n => n.UserId == userId && n.Type == NotificationType.QuoteUpdated)));
        Assert.Contains(_host.Emails.Sent, e => e.Subject.Contains("Báo giá chính thức"));

        // Stale version from another admin tab.
        await Assert.ThrowsAsync<ConflictException>(() => Quotes(q => q.UpdateAsync(submitted.Id,
            new AdminQuoteUpdateCommand { Status = QuoteStatus.Cancelled, Version = detail.Version }, "admin")));

        await Quotes(q => q.RespondAsync(userId, submitted.Code, accept: true));
        Assert.Equal(QuoteStatus.Accepted, (await Quotes(q => q.GetAsync(submitted.Id))).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Quotes(q => q.RecalculateAsync(submitted.Id, new QuoteEstimateRequest { Kind = "tu-quan-ao" },
            _host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().QuoteRequests.Where(x => x.Id == submitted.Id).Select(x => x.Version).SingleAsync()).Result)));
    }

    [Fact]
    public async Task Customer_CanOnlySeeAndChangeTheirOwnQuotes()
    {
        var owner = await AdminServiceTests.CreateCustomerAsync(_host);
        var stranger = await AdminServiceTests.CreateCustomerAsync(_host);
        var submitted = await Quotes(q => q.SubmitAsync(Submit("kệ tivi gỗ sồi dài 1m8"), owner));

        await Assert.ThrowsAsync<NotFoundException>(() => Quotes(q => q.GetMineAsync(stranger, submitted.Code)));
        await Assert.ThrowsAsync<NotFoundException>(() => Quotes(q => q.CancelMineAsync(stranger, submitted.Code)));
        Assert.Empty((await Quotes(q => q.ListMineAsync(stranger, 1))).Items);
        Assert.Single((await Quotes(q => q.ListMineAsync(owner, 1))).Items);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Quotes(q => q.RespondAsync(owner, submitted.Code, true))); // not quoted yet
        await Quotes(q => q.CancelMineAsync(owner, submitted.Code));
        Assert.Equal(QuoteStatus.Cancelled, (await Quotes(q => q.GetMineAsync(owner, submitted.Code))).Status);
    }

    [Fact]
    public async Task Admin_Recalculate_UpdatesTheEstimate()
    {
        var submitted = await Quotes(q => q.SubmitAsync(Submit("bàn ăn gỗ óc chó dài 1m8 rộng 90cm cao 75cm"), null));
        var detail = await Quotes(q => q.GetAsync(submitted.Id));

        var rubberwood = await MaterialIdAsync("go-cao-su");
        await Quotes(q => q.RecalculateAsync(submitted.Id, new QuoteEstimateRequest
        {
            Kind = detail.Spec.Kind, LengthMm = 1800, WidthMm = 900, HeightMm = 750,
            MaterialId = rubberwood, Finish = FinishType.PU, Quantity = 1
        }, detail.Version));

        var updated = await Quotes(q => q.GetAsync(submitted.Id));
        Assert.Equal("Gỗ cao su", updated.Spec.MaterialName);
        Assert.True(updated.Breakdown.UnitPrice < detail.Breakdown.UnitPrice);
        Assert.Equal(updated.Breakdown.UnitPrice, updated.Breakdown.DirectCost + updated.Breakdown.OverheadCost + updated.Breakdown.ProfitAmount);
    }

    [Fact]
    public async Task AdminList_FiltersByStatusAndSearch()
    {
        var a = await Quotes(q => q.SubmitAsync(Submit("bàn trà gỗ sồi"), null));
        var bCommand = Submit("giường gỗ óc chó 1m8");
        bCommand.Name = "Chị Ngọc";
        var b = await Quotes(q => q.SubmitAsync(bCommand, null));
        await Quotes(q => q.UpdateAsync(b.Id, new AdminQuoteUpdateCommand { Status = QuoteStatus.Reviewing, Version = _host.RunAsync(sp => sp.GetRequiredService<IQuoteService>().GetAsync(b.Id)).Result.Version }, "admin"));

        Assert.Equal(a.Code, Assert.Single((await Quotes(q => q.ListAsync(new AdminQuoteQuery { Status = QuoteStatus.New }))).Items).Code);
        Assert.Equal(b.Code, Assert.Single((await Quotes(q => q.ListAsync(new AdminQuoteQuery { Search = "Ngọc" }))).Items).Code);
        Assert.Equal(b.Code, Assert.Single((await Quotes(q => q.ListAsync(new AdminQuoteQuery { Search = b.Code.ToLowerInvariant() }))).Items).Code);
        Assert.Equal(1, await Quotes(q => q.CountNewAsync()));
    }

    [Fact]
    public async Task AssistantEstimate_IsKeptInTheAiHistory()
    {
        var caller = new AiCaller(null, "0123456789abcdef0123456789abcdef");

        var estimate = await _host.RunAsync(sp => sp.GetRequiredService<IAssistantService>().EstimatePriceAsync(caller, new QuoteEstimateRequest { Text = "giường gỗ sồi 1m6" }));

        var conversation = await Db(db => db.AIConversations.Include(c => c.Messages).SingleAsync());
        Assert.Equal(AIConversationType.PriceQuote, conversation.Type);
        Assert.Equal(estimate.Explanation, conversation.Messages.Single(m => m.Role == AIMessageRole.Assistant).Content);
    }
}
