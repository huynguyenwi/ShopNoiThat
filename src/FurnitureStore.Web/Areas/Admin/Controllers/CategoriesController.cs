using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Web.Areas.Admin.Models;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>/admin/categories - two-level category tree.</summary>
public sealed class CategoriesController(ICategoryAdminService categories) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await categories.ListAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(int? parentId, CancellationToken cancellationToken) =>
        View("Form", await BuildFormAsync(null, new CategoryUpsertCommand { ParentId = parentId, IsActive = true }, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Command")] CategoryUpsertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await categories.CreateAsync(command, cancellationToken);
            SetStatus($"Đã tạo danh mục \"{command.Name}\".");
            return RedirectToAction(nameof(Index));
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", await BuildFormAsync(null, command, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(id, cancellationToken);
        var command = new CategoryUpsertCommand
        {
            Name = category.Name,
            Slug = category.Slug,
            ParentId = category.ParentId,
            Description = category.Description,
            IconCssClass = category.IconCssClass,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            ShowOnHomePage = category.ShowOnHomePage,
            MetaTitle = category.MetaTitle,
            MetaDescription = category.MetaDescription
        };
        return View("Form", await BuildFormAsync(id, command, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Command")] CategoryUpsertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await categories.UpdateAsync(id, command, cancellationToken);
            SetStatus($"Đã lưu danh mục \"{command.Name}\".");
            return RedirectToAction(nameof(Index));
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", await BuildFormAsync(id, command, cancellationToken));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await categories.DeleteAsync(id, cancellationToken);
            SetStatus("Đã xóa danh mục.");
        }
        catch (BusinessRuleException ex)
        {
            SetError(ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<CategoryFormViewModel> BuildFormAsync(int? id, CategoryUpsertCommand command, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        return new CategoryFormViewModel
        {
            Id = id,
            Command = command,
            Parents = all.Where(c => c.ParentId is null && c.Id != id).ToList()
        };
    }
}

/// <summary>/admin/attributes/{kind} with kind = colors | materials | sizes | styles.</summary>
[Route("admin/attributes/{kind}")]
public sealed class AttributesController(IAttributeAdminService attributes) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string kind, CancellationToken cancellationToken)
    {
        if (AttributeKinds.FromRoute(kind) is not { } attributeKind)
        {
            return NotFound();
        }

        return View(new AttributeListViewModel { Kind = attributeKind, Items = await attributes.ListAsync(attributeKind, cancellationToken) });
    }

    [HttpGet("create")]
    public IActionResult Create(string kind) =>
        AttributeKinds.FromRoute(kind) is { } attributeKind
            ? View("Form", new AttributeFormViewModel { Kind = attributeKind, Command = new AttributeUpsertCommand { IsActive = true, HexCode = "#B07D4F" } })
            : NotFound();

    [HttpPost("create")]
    public async Task<IActionResult> Create(string kind, [Bind(Prefix = "Command")] AttributeUpsertCommand command, CancellationToken cancellationToken)
    {
        if (AttributeKinds.FromRoute(kind) is not { } attributeKind)
        {
            return NotFound();
        }

        try
        {
            ModelState.ThrowIfBindingFailed();
            await attributes.CreateAsync(attributeKind, command, cancellationToken);
            SetStatus($"Đã thêm \"{command.Name}\".");
            return RedirectToAction(nameof(Index), new { kind });
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", new AttributeFormViewModel { Kind = attributeKind, Command = command });
        }
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(string kind, int id, CancellationToken cancellationToken)
    {
        if (AttributeKinds.FromRoute(kind) is not { } attributeKind)
        {
            return NotFound();
        }

        var item = await attributes.GetAsync(attributeKind, id, cancellationToken);
        var command = new AttributeUpsertCommand
        {
            Name = item.Name,
            Slug = item.Slug,
            DisplayOrder = item.DisplayOrder,
            IsActive = item.IsActive,
            Description = item.Description,
            HexCode = item.HexCode,
            MaterialGroup = item.MaterialGroup,
            LengthMm = item.LengthMm,
            WidthMm = item.WidthMm,
            HeightMm = item.HeightMm,
            FurnitureType = item.FurnitureType,
            Code = item.Code
        };
        return View("Form", new AttributeFormViewModel { Id = id, Kind = attributeKind, Command = command });
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(string kind, int id, [Bind(Prefix = "Command")] AttributeUpsertCommand command, CancellationToken cancellationToken)
    {
        if (AttributeKinds.FromRoute(kind) is not { } attributeKind)
        {
            return NotFound();
        }

        try
        {
            ModelState.ThrowIfBindingFailed();
            await attributes.UpdateAsync(attributeKind, id, command, cancellationToken);
            SetStatus($"Đã lưu \"{command.Name}\".");
            return RedirectToAction(nameof(Index), new { kind });
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", new AttributeFormViewModel { Id = id, Kind = attributeKind, Command = command });
        }
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(string kind, int id, CancellationToken cancellationToken)
    {
        if (AttributeKinds.FromRoute(kind) is not { } attributeKind)
        {
            return NotFound();
        }

        try
        {
            await attributes.DeleteAsync(attributeKind, id, cancellationToken);
            SetStatus("Đã xóa.");
        }
        catch (BusinessRuleException ex)
        {
            SetError(ex.Message);
        }

        return RedirectToAction(nameof(Index), new { kind });
    }
}
