using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

public sealed class HomeController(ICatalogService catalog, IReviewService reviews, IStoreInfoService storeInfo, IHomeBannerService banner) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new HomeViewModel(
            await catalog.GetHomePageAsync(cancellationToken),
            await storeInfo.GetAsync(cancellationToken),
            await reviews.GetLatestAsync(6, cancellationToken),
            await banner.GetAsync(cancellationToken)));
}
