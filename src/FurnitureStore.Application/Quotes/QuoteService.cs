using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.Quotes;

public interface IQuoteService
{
    /// <summary>Parses the request (rule-based, AI when configured), prices it with <see cref="PriceCalculatorService"/> and explains it. Nothing is saved.</summary>
    Task<QuoteEstimateDto> EstimateAsync(QuoteEstimateRequest request, bool explainWithAi, CancellationToken cancellationToken = default);
    Task<QuoteSubmittedDto> SubmitAsync(QuoteSubmitCommand command, string? userId, CancellationToken cancellationToken = default);

    Task<PagedResult<QuoteListItemDto>> ListMineAsync(string userId, int page, CancellationToken cancellationToken = default);
    Task<QuoteDetailDto> GetMineAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task RespondAsync(string userId, string code, bool accept, CancellationToken cancellationToken = default);
    Task CancelMineAsync(string userId, string code, CancellationToken cancellationToken = default);

    Task<PagedResult<QuoteListItemDto>> ListAsync(AdminQuoteQuery query, CancellationToken cancellationToken = default);
    Task<QuoteDetailDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, AdminQuoteUpdateCommand command, string? adminName, CancellationToken cancellationToken = default);
    Task RecalculateAsync(int id, QuoteEstimateRequest spec, Guid version, CancellationToken cancellationToken = default);
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
}

public sealed class QuoteSubmitCommandValidator : AbstractValidator<QuoteSubmitCommand>
{
    public QuoteSubmitCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập họ tên.").MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Vui lòng nhập số điện thoại.")
            .Matches(ValidationPatterns.VietnamesePhone).WithMessage(ValidationPatterns.VietnamesePhoneMessage);
        RuleFor(x => x.Email).EmailAddress().WithMessage("Email không hợp lệ.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Note).MaximumLength(1000).WithMessage("Ghi chú tối đa 1.000 ký tự.");
        RuleFor(x => x.Text).MaximumLength(2000).WithMessage("Mô tả tối đa 2.000 ký tự.");
    }
}

public sealed class QuoteService(
    IPriceRuleRepository priceRules,
    IQuoteRepository quotes,
    IRepository<ProductMaterial> materials,
    ICatalogService catalog,
    IProductFactRepository facts,
    IAiChatClient aiClient,
    INotificationService notifications,
    IEmailSender emailSender,
    IAuditLogService auditLog,
    IValidator<QuoteSubmitCommand> submitValidator,
    IUnitOfWork unitOfWork,
    IOptions<ApplicationSettings> siteOptions,
    Engagement.IStoreInfoService storeInfo,
    TimeProvider timeProvider,
    ILogger<QuoteService> logger) : IQuoteService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // ================================================================== estimate

    public async Task<QuoteEstimateDto> EstimateAsync(QuoteEstimateRequest request, bool explainWithAi, CancellationToken cancellationToken = default)
    {
        if (request.Text?.Length > 2000)
        {
            throw new AppValidationException("Text", "Mô tả tối đa 2.000 ký tự.");
        }

        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var kinds = QuoteKinds.From(options);
        var parsed = QuoteRequestParser.Parse(request.Text, new CatalogVocabulary(options), options);
        if (aiClient.IsConfigured && !string.IsNullOrWhiteSpace(request.Text) && NeedsHelp(parsed, request))
        {
            parsed = await ExtractWithAiAsync(request.Text!, parsed, kinds, options, cancellationToken);
        }

        var (spec, specDto, assumptions) = Resolve(request, parsed, kinds, options);
        var rules = await priceRules.GetActiveAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var breakdown = PriceCalculatorService.Calculate(spec, rules, now);
        var alternatives = await AlternativesAsync(spec, rules, now, cancellationToken);
        var similar = await SimilarProductsAsync(specDto.Kind, cancellationToken);

        var explanation = TemplateExplanation(specDto, breakdown, alternatives);
        if (explainWithAi && aiClient.IsConfigured)
        {
            explanation = await ExplainWithAiAsync(request.Text, specDto, assumptions, breakdown, alternatives, cancellationToken) ?? explanation;
        }

        return new QuoteEstimateDto(specDto, assumptions, breakdown, explanation, alternatives, similar, aiClient.IsConfigured);
    }

    private static bool NeedsHelp(ParsedQuoteRequest parsed, QuoteEstimateRequest request) =>
        (request.Kind is null && parsed.Kind is null)
        || (request.LengthMm is null && parsed.LengthMm is null)
        || (request.MaterialId is null && parsed.MaterialId is null);

    private static (QuoteSpec Spec, QuoteSpecDto Dto, List<string> Assumptions) Resolve(QuoteEstimateRequest request, ParsedQuoteRequest parsed,
        IReadOnlyList<QuoteKind> kinds, FilterOptionsDto options)
    {
        var assumptions = new List<string>();
        var kindSlug = request.Kind ?? parsed.Kind;
        var kind = kinds.FirstOrDefault(k => k.Slug == kindSlug)
                   ?? throw new AppValidationException("Kind", kindSlug is null
                       ? "Chưa xác định được loại sản phẩm, vui lòng chọn loại sản phẩm cần đóng."
                       : "Loại sản phẩm không hợp lệ.");

        int Dimension(int? requested, int? fromText, int typical, string label)
        {
            if ((requested ?? fromText) is int value) return value;
            assumptions.Add($"Chưa có {label}: tạm tính {typical} mm (kích thước phổ biến của {kind.Name.ToLowerInvariant()}).");
            return typical;
        }

        var length = Dimension(request.LengthMm, parsed.LengthMm, kind.LengthMm, "chiều dài");
        var width = Dimension(request.WidthMm, parsed.WidthMm, kind.WidthMm, "chiều rộng / sâu");
        var height = Dimension(request.HeightMm, parsed.HeightMm, kind.HeightMm, "chiều cao");

        var materialId = request.MaterialId ?? parsed.MaterialId;
        OptionDto? material;
        if (materialId is int id)
        {
            material = options.Materials.FirstOrDefault(m => m.Id == id) ?? throw new AppValidationException("MaterialId", "Chất liệu không hợp lệ.");
            if (request.MaterialId is null && !parsed.MaterialIsExplicit)
            {
                assumptions.Add($"Chưa rõ loại gỗ: tạm tính {material.Name.ToLowerInvariant()}.");
            }
        }
        else
        {
            material = options.Materials.FirstOrDefault(m => m.Slug == kind.DefaultMaterialSlug) ?? options.Materials.FirstOrDefault();
            if (material is not null) assumptions.Add($"Chưa chọn chất liệu: tạm tính {material.Name.ToLowerInvariant()}.");
        }

        var colorId = request.ColorId ?? parsed.ColorId;
        var color = colorId is int c ? options.Colors.FirstOrDefault(o => o.Id == c) : null;
        var styleId = request.StyleId ?? parsed.StyleId;
        var style = styleId is int s ? options.Styles.FirstOrDefault(o => o.Id == s) : null;

        var finish = request.Finish ?? parsed.Finish;
        if (finish is null)
        {
            finish = kind.DefaultFinish;
            if (kind.FurnitureType != FurnitureType.Sofa) assumptions.Add($"Chưa chọn kiểu sơn: tạm tính {LowerFirst(PriceCalculatorService.FinishName(finish.Value))}.");
        }

        var quantity = request.Quantity ?? parsed.Quantity ?? 1;
        var spec = new QuoteSpec(kind.FurnitureType, length, width, height, material?.Id, finish.Value, quantity);
        PriceCalculatorService.Validate(spec);

        var dto = new QuoteSpecDto(kind.Slug, kind.Name, kind.FurnitureType, length, width, height, material?.Id, material?.Name,
            color?.Id, color?.Name, style?.Id, style?.Name, finish.Value, PriceCalculatorService.FinishName(finish.Value), quantity);
        return (spec, dto, assumptions);
    }

    /// <summary>The same piece in other materials of the same family (e.g. other woods), cheapest first.</summary>
    private async Task<IReadOnlyList<QuoteAlternativeDto>> AlternativesAsync(QuoteSpec spec, IReadOnlyList<PriceRule> rules, DateTime now, CancellationToken cancellationToken)
    {
        if (spec.MaterialId is null) return [];
        var all = await materials.ListAsync(m => m.IsActive, cancellationToken);
        var current = all.FirstOrDefault(m => m.Id == spec.MaterialId);
        if (current is null) return [];

        var alternatives = new List<QuoteAlternativeDto>();
        foreach (var other in all.Where(m => m.Group == current.Group && m.Id != current.Id))
        {
            try
            {
                var price = PriceCalculatorService.Calculate(spec with { MaterialId = other.Id }, rules, now);
                alternatives.Add(new QuoteAlternativeDto(other.Id, other.Name, price.UnitPrice, price.Total));
            }
            catch (BusinessRuleException)
            {
                // No price for that material: not offered.
            }
        }

        return alternatives.OrderBy(a => a.UnitPrice).Take(3).ToList();
    }

    private async Task<IReadOnlyList<ProductRecommendationDto>> SimilarProductsAsync(string kind, CancellationToken cancellationToken)
    {
        var page = await catalog.GetProductsAsync(new ProductQuery { CategorySlug = kind, Sort = ProductSort.BestSelling, PageSize = 3 }, cancellationToken);
        var productFacts = await facts.GetFactsAsync(page.Items.Select(p => p.Id).ToList(), cancellationToken);
        return productFacts.Select(p => AssistantService.ToCard(p, "Mẫu có sẵn để tham khảo")).ToList();
    }

    private static string TemplateExplanation(QuoteSpecDto spec, PriceBreakdown b, IReadOnlyList<QuoteAlternativeDto> alternatives)
    {
        var what = $"{spec.TypeName.ToLowerInvariant()}{(spec.MaterialName is null ? "" : " " + spec.MaterialName.ToLowerInvariant())} {spec.LengthMm} x {spec.WidthMm} x {spec.HeightMm} mm, {LowerFirst(spec.FinishName)}";
        var materialShare = b.UnitPrice == 0 ? 0 : (int)Math.Round(b.MaterialCost / b.UnitPrice * 100);
        var text = $"Giá dự kiến cho {what}: {AiPrompts.Vnd(b.UnitPrice)}/sản phẩm"
                   + (b.Quantity > 1 ? $", {b.Quantity} sản phẩm là {AiPrompts.Vnd(b.Total)}" : "") + ". "
                   + $"Vật liệu {AiPrompts.Vnd(b.MaterialCost)} (khoảng {materialShare}% giá), công gia công {AiPrompts.Vnd(b.LaborCost)}, "
                   + $"hoàn thiện và sơn {AiPrompts.Vnd(b.FinishingCost + b.PaintCost)}, phụ kiện {AiPrompts.Vnd(b.AccessoryCost)}. "
                   + "Đây là giá tham khảo tính từ bảng giá của xưởng; cửa hàng sẽ liên hệ để thống nhất chi tiết và xác nhận giá cuối cùng.";
        var cheaper = alternatives.FirstOrDefault(a => a.UnitPrice < b.UnitPrice);
        if (cheaper is not null)
        {
            text += $" Nếu muốn tiết kiệm, cùng kích thước dùng {cheaper.MaterialName.ToLowerInvariant()} có giá dự kiến {AiPrompts.Vnd(cheaper.UnitPrice)}/sản phẩm.";
        }

        return text;
    }

    // ------------------------------------------------------------------ AI helpers (optional)

    private async Task<ParsedQuoteRequest> ExtractWithAiAsync(string text, ParsedQuoteRequest parsed, IReadOnlyList<QuoteKind> kinds, FilterOptionsDto options, CancellationToken cancellationToken)
    {
        var system = "Bạn trích xuất thông số từ yêu cầu đặt đóng nội thất của khách (tiếng Việt). Không đoán nếu khách không nói. "
                     + "LOẠI (slug: tên): " + string.Join("; ", kinds.Select(k => $"{k.Slug}: {k.Name}"))
                     + ". CHẤT LIỆU: " + string.Join("; ", options.Materials.Select(m => $"{m.Slug}: {m.Name}"))
                     + ". MÀU: " + string.Join("; ", options.Colors.Select(c => $"{c.Slug}: {c.Name}"))
                     + ". PHONG CÁCH: " + string.Join("; ", options.Styles.Select(s => $"{s.Slug}: {s.Name}"))
                     + ". KIỂU SƠN: NaturalOil, PU, NC, TwoK, Lacquer. "
                     + """Trả về DUY NHẤT JSON: {"kind":slug|null,"lengthMm":số|null,"widthMm":số|null,"heightMm":số|null,"material":slug|null,"color":slug|null,"style":slug|null,"finish":tên|null,"quantity":số|null}. Kích thước đổi ra milimét.""";
        try
        {
            var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                [new AiChatMessage(AIMessageRole.System, system), new AiChatMessage(AIMessageRole.User, text)]), cancellationToken);
            if (!ModelJson.TryParse(completion.Content, out var root)) return parsed;

            int? Mm(string name) => ModelJson.Int(root, name) is int v && v is > 0 and <= PriceCalculatorService.MaxLengthMm ? v : null;
            int? IdOf(IReadOnlyList<OptionDto> list, string name) => ModelJson.String(root, name) is { } slug ? list.FirstOrDefault(o => o.Slug == slug)?.Id : null;

            var kind = ModelJson.String(root, "kind") is { } k && kinds.Any(x => x.Slug == k) ? k : null;
            var finish = Enum.TryParse<FinishType>(ModelJson.String(root, "finish"), out var f) && Enum.IsDefined(f) ? f : (FinishType?)null;
            var quantity = ModelJson.Int(root, "quantity") is int q && q is > 0 and <= PriceCalculatorService.MaxQuantity ? q : (int?)null;
            var aiMaterial = IdOf(options.Materials, "material");

            // Numbers read by the rule-based parser win; the model only fills the gaps.
            return new ParsedQuoteRequest(
                parsed.Kind ?? kind, parsed.LengthMm ?? Mm("lengthMm"), parsed.WidthMm ?? Mm("widthMm"), parsed.HeightMm ?? Mm("heightMm"),
                parsed.MaterialId ?? aiMaterial, parsed.MaterialIsExplicit || (parsed.MaterialId is null && aiMaterial is not null),
                parsed.ColorId ?? IdOf(options.Colors, "color"), parsed.StyleId ?? IdOf(options.Styles, "style"),
                parsed.Finish ?? finish, parsed.Quantity ?? quantity);
        }
        catch (AiUnavailableException ex)
        {
            logger.LogWarning("AI quote extraction failed: {Reason}", ex.Message);
            return parsed;
        }
    }

    private async Task<string?> ExplainWithAiAsync(string? request, QuoteSpecDto spec, IReadOnlyList<string> assumptions, PriceBreakdown b,
        IReadOnlyList<QuoteAlternativeDto> alternatives, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(new
        {
            sanPham = $"{spec.TypeName} {spec.MaterialName} {spec.LengthMm}x{spec.WidthMm}x{spec.HeightMm}mm, {spec.FinishName}, số lượng {spec.Quantity}",
            giaDuKien = new
            {
                vatLieu = AiPrompts.Vnd(b.MaterialCost), giaCong = AiPrompts.Vnd(b.LaborCost), hoanThien = AiPrompts.Vnd(b.FinishingCost),
                son = AiPrompts.Vnd(b.PaintCost), phuKien = AiPrompts.Vnd(b.AccessoryCost), chiPhiChung = AiPrompts.Vnd(b.OverheadCost),
                loiNhuan = AiPrompts.Vnd(b.ProfitAmount), giaMotSanPham = AiPrompts.Vnd(b.UnitPrice), tongCong = AiPrompts.Vnd(b.Total)
            },
            giaDinh = assumptions,
            phuongAnKhac = alternatives.Select(a => new { chatLieu = a.MaterialName, giaMotSanPham = AiPrompts.Vnd(a.UnitPrice) })
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) });

        var brand = (await storeInfo.GetAsync(cancellationToken)).BrandName;
        var system = $"Bạn là tư vấn viên của xưởng nội thất {brand}, giải thích báo giá dự kiến cho khách bằng tiếng Việt (80 - 150 từ). "
                     + "QUY TẮC: giá do hệ thống tính - KHÔNG được thay đổi, làm tròn khác hay tự tính thêm con số nào; chỉ dùng đúng số trong DỮ LIỆU. "
                     + "Không hứa giảm giá. Nêu rõ đây là giá tham khảo, cửa hàng sẽ xác nhận giá cuối cùng. Có thể gợi ý phương án khác trong DỮ LIỆU để tiết kiệm. "
                     + "Nếu có giả định (kích thước / chất liệu tạm tính) hãy nhắc khách kiểm tra lại. "
                     + """Trả về DUY NHẤT JSON: {"explanation":"..."}. DỮ LIỆU: """ + data;
        try
        {
            var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                [new AiChatMessage(AIMessageRole.System, system), new AiChatMessage(AIMessageRole.User, request ?? "Giải thích báo giá giúp tôi")]), cancellationToken);
            var text = ModelJson.TryParse(completion.Content, out var root) ? ModelJson.String(root, "explanation") : completion.Content;
            if (string.IsNullOrWhiteSpace(text)) return null;

            var allowed = b.Amounts().Concat(alternatives.SelectMany(a => new[] { a.UnitPrice, a.Total })).Concat(PriceGuard.AmountsIn(request)).ToList();
            var safe = PriceGuard.Sanitize(text.Trim(), allowed, out var replaced);
            if (replaced > 0) logger.LogWarning("Removed {Count} invented amount(s) from an AI quote explanation", replaced);
            return safe.Length > 4000 ? safe[..4000] : safe;
        }
        catch (AiUnavailableException ex)
        {
            logger.LogWarning("AI quote explanation failed: {Reason}", ex.Message);
            return null;
        }
    }

    // ================================================================== submit & customer side

    public async Task<QuoteSubmittedDto> SubmitAsync(QuoteSubmitCommand command, string? userId, CancellationToken cancellationToken = default)
    {
        await submitValidator.EnsureValidAsync(command, cancellationToken);
        var estimate = await EstimateAsync(command, explainWithAi: true, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var spec = estimate.Spec;
        var b = estimate.Breakdown;

        var quote = new QuoteRequest
        {
            QuoteCode = await GenerateCodeAsync(now, cancellationToken),
            UserId = userId,
            CustomerName = command.Name.Trim(),
            Phone = command.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim(),
            RawRequest = string.IsNullOrWhiteSpace(command.Text) ? Summary(spec) : command.Text.Trim(),
            CustomerNote = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            Status = QuoteStatus.New,
            AiExplanation = estimate.Explanation
        };
        Apply(quote, spec, b);
        await quotes.AddAsync(quote, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.NewQuoteRequest, $"Yêu cầu báo giá {quote.QuoteCode}",
            $"{quote.CustomerName}: {Summary(spec)} - dự kiến {AiPrompts.Vnd(b.Total)}", $"/admin/quotes/{quote.Id}", cancellationToken);
        if (quote.Email is not null)
        {
            await TryEmailAsync(quote.Email, quote.CustomerName, $"Đã nhận yêu cầu báo giá {quote.QuoteCode}",
                EmailTemplates.QuoteReceived((await storeInfo.GetAsync(cancellationToken)).Name, quote.CustomerName, quote.QuoteCode, Summary(spec), b.UnitPrice, b.Quantity, b.Total,
                    userId is null ? null : Url($"/account/quotes/{quote.QuoteCode}")));
        }

        logger.LogInformation("Quote request {QuoteCode} submitted ({Total} VND estimate)", quote.QuoteCode, b.Total);
        return new QuoteSubmittedDto(quote.Id, quote.QuoteCode, b.UnitPrice, b.Total);
    }

    public Task<PagedResult<QuoteListItemDto>> ListMineAsync(string userId, int page, CancellationToken cancellationToken = default) =>
        quotes.SearchAsync(new AdminQuoteQuery { Page = Math.Max(1, page), PageSize = 20 }, userId, cancellationToken);

    public async Task<QuoteDetailDto> GetMineAsync(string userId, string code, CancellationToken cancellationToken = default) =>
        await MapAsync(await OwnedAsync(userId, code, cancellationToken), customerView: true, cancellationToken);

    public async Task RespondAsync(string userId, string code, bool accept, CancellationToken cancellationToken = default)
    {
        var quote = await OwnedAsync(userId, code, cancellationToken);
        if (quote.Status != QuoteStatus.Quoted)
        {
            throw new BusinessRuleException("Báo giá này chưa có giá chính thức hoặc đã được xử lý.");
        }

        QuoteStatusTransitions.Apply(quote, accept ? QuoteStatus.Accepted : QuoteStatus.Rejected, timeProvider.GetUtcNow().UtcDateTime, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.QuoteUpdated,
            $"Khách {(accept ? "đồng ý" : "từ chối")} báo giá {quote.QuoteCode}", $"{quote.CustomerName} - {quote.Phone}", $"/admin/quotes/{quote.Id}", cancellationToken);
    }

    public async Task CancelMineAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var quote = await OwnedAsync(userId, code, cancellationToken);
        if (quote.Status is not (QuoteStatus.New or QuoteStatus.Reviewing))
        {
            throw new BusinessRuleException("Chỉ hủy được yêu cầu đang chờ báo giá.");
        }

        QuoteStatusTransitions.Apply(quote, QuoteStatus.Cancelled, timeProvider.GetUtcNow().UtcDateTime, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<QuoteRequest> OwnedAsync(string userId, string code, CancellationToken cancellationToken)
    {
        var quote = await quotes.GetByCodeAsync(code, cancellationToken);
        return quote is not null && quote.UserId == userId ? quote : throw new NotFoundException("báo giá", code);
    }

    // ================================================================== admin

    public Task<PagedResult<QuoteListItemDto>> ListAsync(AdminQuoteQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return quotes.SearchAsync(query, null, cancellationToken);
    }

    public async Task<QuoteDetailDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await MapAsync(await quotes.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("báo giá", id), customerView: false, cancellationToken);

    public async Task UpdateAsync(int id, AdminQuoteUpdateCommand command, string? adminName, CancellationToken cancellationToken = default)
    {
        var quote = await quotes.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("báo giá", id);
        if (quote.Version != command.Version)
        {
            throw new ConflictException("Báo giá vừa được cập nhật bởi người khác. Vui lòng tải lại trang.");
        }

        if (command.FinalQuotedPrice is decimal price)
        {
            if (price is <= 0 or > 100_000_000_000m)
            {
                throw new AppValidationException("FinalQuotedPrice", "Giá báo phải lớn hơn 0.");
            }

            quote.FinalQuotedPrice = Math.Round(price, 0);
        }

        quote.AdminNote = string.IsNullOrWhiteSpace(command.AdminNote) ? null : command.AdminNote.Trim()[..Math.Min(command.AdminNote.Trim().Length, 1000)];
        var previous = quote.Status;
        QuoteStatusTransitions.Apply(quote, command.Status, timeProvider.GetUtcNow().UtcDateTime, adminName);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(previous == quote.Status ? AuditAction.Update : AuditAction.StatusChange, nameof(QuoteRequest), id.ToString(),
            $"{quote.QuoteCode}: {QuoteStatusTransitions.DisplayName(quote.Status)}", NewValues: new { quote.Status, quote.FinalQuotedPrice }), cancellationToken);

        if (previous != quote.Status)
        {
            if (quote.UserId is not null)
            {
                await notifications.NotifyUserAsync(quote.UserId, NotificationType.QuoteUpdated, $"Báo giá {quote.QuoteCode}: {QuoteStatusTransitions.DisplayName(quote.Status)}",
                    quote.Status == QuoteStatus.Quoted ? $"Giá chính thức: {AiPrompts.Vnd(quote.FinalQuotedPrice!.Value)}" : "Trạng thái yêu cầu báo giá đã thay đổi.",
                    $"/account/quotes/{quote.QuoteCode}", cancellationToken);
            }

            if (quote.Status == QuoteStatus.Quoted && quote.Email is not null)
            {
                await TryEmailAsync(quote.Email, quote.CustomerName, $"Báo giá chính thức {quote.QuoteCode}",
                    EmailTemplates.QuoteAnswered((await storeInfo.GetAsync(cancellationToken)).Name, quote.CustomerName, quote.QuoteCode, quote.FinalQuotedPrice!.Value, quote.AdminNote,
                        quote.UserId is null ? null : Url($"/account/quotes/{quote.QuoteCode}")));
            }
        }
    }

    public async Task RecalculateAsync(int id, QuoteEstimateRequest spec, Guid version, CancellationToken cancellationToken = default)
    {
        var quote = await quotes.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("báo giá", id);
        if (quote.Version != version)
        {
            throw new ConflictException("Báo giá vừa được cập nhật bởi người khác. Vui lòng tải lại trang.");
        }

        if (quote.Status is QuoteStatus.Accepted or QuoteStatus.Cancelled)
        {
            throw new BusinessRuleException("Không thể tính lại báo giá đã chốt hoặc đã hủy.");
        }

        spec.Text = null; // admins edit the structured fields only
        var estimate = await EstimateAsync(spec, explainWithAi: false, cancellationToken);
        Apply(quote, estimate.Spec, estimate.Breakdown);
        quote.AiExplanation = estimate.Explanation;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(QuoteRequest), id.ToString(), $"Tính lại báo giá {quote.QuoteCode}",
            NewValues: new { quote.LengthMm, quote.WidthMm, quote.HeightMm, quote.MaterialName, quote.Quantity, quote.EstimatedTotal }), cancellationToken);
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) => quotes.CountNewAsync(cancellationToken);

    // ================================================================== mapping

    private static void Apply(QuoteRequest quote, QuoteSpecDto spec, PriceBreakdown b)
    {
        quote.FurnitureType = spec.FurnitureType;
        quote.ProductTypeName = spec.TypeName;
        quote.LengthMm = spec.LengthMm;
        quote.WidthMm = spec.WidthMm;
        quote.HeightMm = spec.HeightMm;
        quote.MaterialId = spec.MaterialId;
        quote.MaterialName = spec.MaterialName;
        quote.ColorName = spec.ColorName;
        quote.StyleId = spec.StyleId;
        quote.FinishType = spec.Finish;
        quote.Quantity = spec.Quantity;
        quote.MaterialCost = b.MaterialCost;
        quote.LaborCost = b.LaborCost;
        quote.FinishingCost = b.FinishingCost;
        quote.PaintCost = b.PaintCost;
        quote.AccessoryCost = b.AccessoryCost;
        quote.OverheadCost = b.OverheadCost;
        quote.ProfitAmount = b.ProfitAmount;
        quote.EstimatedUnitPrice = b.UnitPrice;
        quote.EstimatedTotal = b.Total;
    }

    private async Task<QuoteDetailDto> MapAsync(QuoteRequest q, bool customerView, CancellationToken cancellationToken)
    {
        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var kind = QuoteKinds.From(options).FirstOrDefault(k => k.Name == q.ProductTypeName);
        var finish = q.FinishType ?? FinishType.PU;
        var spec = new QuoteSpecDto(kind?.Slug ?? string.Empty, q.ProductTypeName, q.FurnitureType, q.LengthMm, q.WidthMm, q.HeightMm,
            q.MaterialId, q.MaterialName, options.Colors.FirstOrDefault(c => c.Name == q.ColorName)?.Id, q.ColorName,
            q.StyleId, options.Styles.FirstOrDefault(s => s.Id == q.StyleId)?.Name, finish, PriceCalculatorService.FinishName(finish), q.Quantity);

        decimal l = q.LengthMm / 1000m, w = q.WidthMm / 1000m, h = q.HeightMm / 1000m;
        var direct = q.MaterialCost + q.LaborCost + q.FinishingCost + q.PaintCost + q.AccessoryCost;
        var breakdown = new PriceBreakdown(Math.Round(2 * (l * w + l * h + w * h), 2), 0, q.MaterialCost, q.LaborCost, q.FinishingCost, q.PaintCost,
            q.AccessoryCost, direct, direct == 0 ? 0 : Math.Round(q.OverheadCost / direct * 100, 1), q.OverheadCost,
            direct + q.OverheadCost == 0 ? 0 : Math.Round(q.ProfitAmount / (direct + q.OverheadCost) * 100, 1), q.ProfitAmount,
            q.EstimatedUnitPrice, q.Quantity, q.EstimatedTotal, []);

        return new QuoteDetailDto(q.Id, q.QuoteCode, q.UserId, q.CustomerName, q.Phone, q.Email, q.RawRequest, spec, breakdown, q.FinalQuotedPrice,
            q.Status, q.AiExplanation, q.CustomerNote, customerView ? null : q.AdminNote, q.CreatedAt, q.QuotedAt, customerView ? null : q.QuotedBy,
            q.Version, customerView ? [] : QuoteStatusTransitions.NextStatuses(q.Status));
    }

    /// <summary>"Sơn PU" → "sơn PU" (keeps abbreviations such as PU / 2K / NC).</summary>
    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    public static string Summary(QuoteSpecDto spec) =>
        $"{spec.Quantity} x {spec.TypeName}{(spec.MaterialName is null ? "" : " " + spec.MaterialName.ToLowerInvariant())} {spec.LengthMm}x{spec.WidthMm}x{spec.HeightMm}mm";

    private async Task<string> GenerateCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = string.Create(5, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            });
            var code = $"BG{now.ToString("yyMMdd", CultureInfo.InvariantCulture)}-{suffix}";
            if (!await quotes.CodeExistsAsync(code, cancellationToken)) return code;
        }

        throw new ConflictException("Không tạo được mã báo giá, vui lòng thử lại.");
    }

    private string Url(string path) => siteOptions.Value.BaseUrl.TrimEnd('/') + path;

    private async Task TryEmailAsync(string to, string name, string subject, string html)
    {
        try
        {
            await emailSender.SendAsync(new EmailMessage(to, subject, html, name));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send quote email");
        }
    }
}
