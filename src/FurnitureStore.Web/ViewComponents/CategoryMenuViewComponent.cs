using FurnitureStore.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.ViewComponents;

/// <summary>Header "Danh mục" mega menu built from the (cached) category tree.</summary>
public sealed class CategoryMenuViewComponent(ICatalogService catalog) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await catalog.GetCategoryTreeAsync(HttpContext.RequestAborted));
}
