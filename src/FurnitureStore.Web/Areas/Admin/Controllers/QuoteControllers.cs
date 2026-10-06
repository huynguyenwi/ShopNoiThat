using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

public sealed record AdminQuoteDetailViewModel(QuoteDetailDto Quote, IReadOnlyList<QuoteKind> Kinds, FilterOptionsDto Options);

/// <summary>/admin/quotes - custom furniture quote requests: review, set the official price, recalculate.</summary>
[Route("admin/quotes")]
public sealed class QuotesController(IQuoteService quotes, ICatalogService catalog) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] AdminQuoteQuery query, CancellationToken cancellationToken)
    {
        ViewData["Query"] = query;
        return View(await quotes.ListAsync(query, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        return View(new AdminQuoteDetailViewModel(await quotes.GetAsync(id, cancellationToken), QuoteKinds.From(options), options));
    }

    [HttpPost("{id:int}/update")]
    public async Task<IActionResult> Update(int id, AdminQuoteUpdateCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await quotes.UpdateAsync(id, command, User.Identity?.Name, cancellationToken);
            SetStatus("Đã cập nhật báo giá.");
        }
        catch (Exception ex) when (ex is DomainException or ConflictException or BusinessRuleException or AppValidationException)
        {
            SetError(ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        return Redirect($"/admin/quotes/{id}");
    }

    [HttpPost("{id:int}/recalculate")]
    public async Task<IActionResult> Recalculate(int id, QuoteEstimateRequest spec, Guid version, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await quotes.RecalculateAsync(id, spec, version, cancellationToken);
            SetStatus("Đã tính lại giá dự kiến theo thông số mới.");
        }
        catch (Exception ex) when (ex is ConflictException or BusinessRuleException or AppValidationException)
        {
            SetError(ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        return Redirect($"/admin/quotes/{id}");
    }
}

public sealed record PriceRuleFormViewModel(int? Id, PriceRuleCommand Command, FilterOptionsDto Options);

/// <summary>/admin/price-rules - parameters of the custom-furniture price calculator.</summary>
[Route("admin/price-rules")]
public sealed class PriceRulesController(IPriceRuleAdminService rules, ICatalogService catalog) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(PriceRuleType? type, CancellationToken cancellationToken)
    {
        ViewData["Type"] = type;
        return View(await rules.ListAsync(type, cancellationToken));
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(PriceRuleType? type, CancellationToken cancellationToken) =>
        View("Form", new PriceRuleFormViewModel(null, new PriceRuleCommand { RuleType = type ?? PriceRuleType.MaterialCostPerSquareMeter, Unit = "đ/m²" },
            await catalog.GetFilterOptionsAsync(cancellationToken)));

    [HttpPost("create")]
    public Task<IActionResult> Create(PriceRuleCommand command, CancellationToken cancellationToken) => SaveAsync(null, command, cancellationToken);

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken) =>
        View("Form", new PriceRuleFormViewModel(id, await rules.GetAsync(id, cancellationToken), await catalog.GetFilterOptionsAsync(cancellationToken)));

    [HttpPost("{id:int}/edit")]
    public Task<IActionResult> Edit(int id, PriceRuleCommand command, CancellationToken cancellationToken) => SaveAsync(id, command, cancellationToken);

    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> Active(int id, bool active, CancellationToken cancellationToken)
    {
        await rules.SetActiveAsync(id, active, cancellationToken);
        SetStatus(active ? "Đã bật tham số giá." : "Đã tắt tham số giá.");
        return Redirect("/admin/price-rules");
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await rules.DeleteAsync(id, cancellationToken);
        SetStatus("Đã xóa tham số giá.");
        return Redirect("/admin/price-rules");
    }

    private async Task<IActionResult> SaveAsync(int? id, PriceRuleCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await rules.SaveAsync(id, command, cancellationToken);
            SetStatus(id is null ? "Đã thêm tham số giá." : "Đã lưu tham số giá.");
            return Redirect("/admin/price-rules?type=" + command.RuleType);
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex, prefix: "Command");
            return View("Form", new PriceRuleFormViewModel(id, command, await catalog.GetFilterOptionsAsync(cancellationToken)));
        }
    }
}
