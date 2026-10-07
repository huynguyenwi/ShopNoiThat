using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>/admin/banner - texts, buttons, statistics and picture of the banner at the top of the home page.</summary>
public sealed class BannerController(IHomeBannerService banners, IOptions<StorageSettings> storageOptions, IOptions<ApplicationSettings> siteOptions)
    : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var banner = await banners.GetAsync(cancellationToken);
        SetPageData(banner);
        return View(HomeBannerCommand.From(banner));
    }

    [HttpPost]
    [RequestSizeLimit(UploadLimits.PerImageBytes + UploadLimits.FormFieldsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.PerImageBytes + UploadLimits.FormFieldsBytes)]
    public async Task<IActionResult> Index(HomeBannerCommand command, IFormFile? image, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            if (image is { Length: > 0 })
            {
                await using var stream = image.OpenReadStream();
                await banners.UpdateAsync(command, (stream, image.FileName), cancellationToken);
            }
            else
            {
                await banners.UpdateAsync(command, null, cancellationToken);
            }

            SetStatus("Đã lưu banner trang chủ.");
            return RedirectToAction(nameof(Index));
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex, prefix: string.Empty);
            SetPageData(await banners.GetAsync(cancellationToken));
            return View(command);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        await banners.ResetAsync(cancellationToken);
        SetStatus("Đã khôi phục banner mặc định.");
        return RedirectToAction(nameof(Index));
    }

    private void SetPageData(HomeBannerDto saved)
    {
        ViewData["Saved"] = saved;
        ViewData["MaxImageMb"] = storageOptions.Value.MaxFileSizeMb;
        var focus = siteOptions.Value.FocusCategorySlug;
        ViewData["FocusUrl"] = string.IsNullOrWhiteSpace(focus) ? "/products" : $"/products?category={focus}";
    }
}
