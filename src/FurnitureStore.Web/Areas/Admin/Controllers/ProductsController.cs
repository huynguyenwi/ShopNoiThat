using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Web.Areas.Admin.Models;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>/admin/products - product, variant, stock, price and image management.</summary>
public sealed class ProductsController(IProductAdminService products, IStoreInfoService storeInfo, ILogger<ProductsController> logger) : AdminControllerBase
{
    public const int MaxQrLabels = 100;

    private const string VariantKeysField = "Command.Variants.Index";
    private const string VariantImagePrefix = "VariantImage_";

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminProductQuery query, CancellationToken cancellationToken)
    {
        var result = await products.ListAsync(query, cancellationToken);
        var lookups = await products.GetLookupsAsync(cancellationToken);
        return View(new AdminProductListViewModel { Query = query, Result = result, Lookups = lookups });
    }

    /// <summary>Printable sheet of QR labels: one product (?id=) or the products matching the list filters (at most 100).</summary>
    [HttpGet]
    public async Task<IActionResult> QrLabels([FromQuery] AdminProductQuery query, int? id, CancellationToken cancellationToken)
    {
        List<QrLabel> labels;
        int total;
        if (id is int productId)
        {
            var product = await products.GetDetailAsync(productId, cancellationToken);
            labels = [new QrLabel(product.Id, product.Name, product.Sku, product.Price, product.OriginalPrice)];
            total = 1;
        }
        else
        {
            query.Page = 1;
            query.PageSize = MaxQrLabels;
            var result = await products.ListAsync(query, cancellationToken);
            labels = result.Items.Select(p => new QrLabel(p.Id, p.Name, p.Sku, p.Price, p.OriginalPrice)).ToList();
            total = result.TotalCount;
        }

        var store = await storeInfo.GetAsync(cancellationToken);
        return View(new QrLabelsViewModel(labels, total, store.Name, store.Hotline, id is int one ? $"/admin/products/edit/{one}" : "/admin/products"));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await products.GetDetailAsync(id, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var command = new ProductUpsertCommand
        {
            Status = ProductStatus.Draft,
            Variants = [new VariantUpsertModel { IsDefault = true, IsActive = true }]
        };
        return View("Form", await BuildFormAsync(command, null, cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    public async Task<IActionResult> Create([Bind(Prefix = "Command")] ProductUpsertCommand command, string? defaultVariantKey,
        List<IFormFile>? images, CancellationToken cancellationToken)
    {
        var keys = ApplyDefaultVariant(command, defaultVariantKey);
        try
        {
            ModelState.ThrowIfBindingFailed();
            var saved = await products.CreateAsync(command, cancellationToken);
            var imageErrors = await UploadImagesAsync(saved, keys, images, cancellationToken);
            SetStatus(imageErrors is null ? $"Đã tạo sản phẩm \"{command.Name}\"." : $"Đã tạo sản phẩm nhưng một số ảnh không hợp lệ: {imageErrors}");
            return RedirectToAction(nameof(Edit), new { id = saved.ProductId });
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", await BuildFormAsync(command, null, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var edit = await products.GetForEditAsync(id, cancellationToken);
        return View("Form", await BuildFormAsync(edit.Command, edit, cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Command")] ProductUpsertCommand command, string? defaultVariantKey,
        List<IFormFile>? images, CancellationToken cancellationToken)
    {
        var keys = ApplyDefaultVariant(command, defaultVariantKey);
        try
        {
            ModelState.ThrowIfBindingFailed();
            var saved = await products.UpdateAsync(id, command, cancellationToken);
            var imageErrors = await UploadImagesAsync(saved, keys, images, cancellationToken);
            SetStatus(imageErrors is null ? $"Đã lưu sản phẩm \"{command.Name}\"." : $"Đã lưu sản phẩm nhưng một số ảnh không hợp lệ: {imageErrors}");
            return RedirectToAction(nameof(Edit), new { id });
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
        }
        catch (ConflictException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        var current = await products.GetForEditAsync(id, cancellationToken);
        return View("Form", await BuildFormAsync(command, current, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await products.DeleteAsync(id, cancellationToken);
        SetStatus("Đã xóa sản phẩm. Lịch sử đơn hàng liên quan vẫn được giữ nguyên.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Status(int id, ProductStatus status, string? returnUrl, CancellationToken cancellationToken)
    {
        await products.SetStatusAsync(id, status, cancellationToken);
        SetStatus("Đã cập nhật trạng thái sản phẩm.");
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequestSizeLimit(UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.PerImageBytes * ProductAdminService.MaxImagesPerUpload + UploadLimits.FormFieldsBytes)]
    public async Task<IActionResult> UploadImages(int id, List<IFormFile>? images, int? variantId, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            var uploads = (images ?? []).Where(f => f.Length > 0).Select(f => new ImageUpload(f.OpenReadStream(), f.FileName, variantId)).ToList();
            var added = await products.AddImagesAsync(id, uploads, cancellationToken);
            SetStatus($"Đã tải lên {added.Count} ảnh.");
        }
        catch (AppValidationException ex)
        {
            SetError(string.Join(" ", ex.Errors));
        }

        return Redirect(Url.Action(nameof(Edit), new { id }) + "#images");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken cancellationToken)
    {
        await products.DeleteImageAsync(id, imageId, cancellationToken);
        SetStatus("Đã xóa ảnh.");
        return Redirect(Url.Action(nameof(Edit), new { id }) + "#images");
    }

    [HttpPost]
    public async Task<IActionResult> PrimaryImage(int id, int imageId, CancellationToken cancellationToken)
    {
        await products.SetPrimaryImageAsync(id, imageId, cancellationToken);
        SetStatus("Đã đặt ảnh đại diện.");
        return Redirect(Url.Action(nameof(Edit), new { id }) + "#images");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ProductFormViewModel> BuildFormAsync(ProductUpsertCommand command, ProductEditDto? current, CancellationToken cancellationToken) => new()
    {
        Id = current?.Id,
        Command = command,
        Lookups = await products.GetLookupsAsync(cancellationToken),
        Images = current?.Images ?? [],
        Slug = current?.Slug,
        UpdatedAt = current?.UpdatedAt,
        UpdatedBy = current?.UpdatedBy
    };

    /// <summary>
    /// Variant rows use arbitrary keys ("Command.Variants.Index"), so rows can be added / removed in the browser.
    /// Returns the keys in posted order (same order as command.Variants) and marks the chosen default row.
    /// </summary>
    private List<string> ApplyDefaultVariant(ProductUpsertCommand command, string? defaultVariantKey)
    {
        var keys = Request.HasFormContentType ? Request.Form[VariantKeysField].Select(k => k ?? string.Empty).ToList() : [];
        for (var i = 0; i < command.Variants.Count; i++)
        {
            command.Variants[i].IsDefault = i < keys.Count && keys[i] == defaultVariantKey;
        }

        return keys;
    }

    /// <summary>Uploads new gallery images and per-variant images after the product was saved. Returns error text, if any.</summary>
    private async Task<string?> UploadImagesAsync(ProductSavedResult saved, IReadOnlyList<string> keys, List<IFormFile>? images, CancellationToken cancellationToken)
    {
        var uploads = new List<ImageUpload>();
        uploads.AddRange((images ?? []).Where(f => f.Length > 0).Select(f => new ImageUpload(f.OpenReadStream(), f.FileName)));

        for (var i = 0; i < keys.Count && i < saved.VariantIds.Count; i++)
        {
            var file = Request.Form.Files.GetFile(VariantImagePrefix + keys[i]);
            if (file is { Length: > 0 })
            {
                uploads.Add(new ImageUpload(file.OpenReadStream(), file.FileName, saved.VariantIds[i]));
            }
        }

        if (uploads.Count == 0)
        {
            return null;
        }

        try
        {
            foreach (var batch in uploads.Chunk(ProductAdminService.MaxImagesPerUpload))
            {
                await products.AddImagesAsync(saved.ProductId, batch, cancellationToken);
            }

            return null;
        }
        catch (AppValidationException ex)
        {
            logger.LogWarning("Image upload rejected for product {ProductId}: {Errors}", saved.ProductId, string.Join("; ", ex.Errors));
            return string.Join(" ", ex.Errors);
        }
    }
}
