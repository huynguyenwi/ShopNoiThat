using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FurnitureStore.Web.Controllers;

[Route("products")]
public sealed class ProductsController(ICatalogService catalog, IReviewService reviews) : Controller
{
    public const string ReviewMessageKey = "ReviewMessage";

    /// <summary>GET /products - list with search, filters, sort and paging. AJAX requests get only the results partial.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ProductListRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.GetProductsAsync(request.ToQuery(), cancellationToken);
        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : await catalog.FindCategoryAsync(request.Category, cancellationToken);

        var model = new ProductListViewModel { Request = request, Result = result, Options = options, Category = category };

        if (HttpContext.IsApiRequest())
        {
            return PartialView("_ProductResults", model);
        }

        return View(model);
    }

    /// <summary>GET /products/{slug}?variant={id}&amp;reviewPage={n}</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug, int? variant, int? reviewPage, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductBySlugAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        await catalog.RecordViewAsync(product.Id, cancellationToken);
        return View(await BuildDetailAsync(product, variant, reviewPage ?? 1, null, cancellationToken));
    }

    /// <summary>GET /products/{slug}/reviews?page=n - review list fragment for AJAX paging.</summary>
    [HttpGet("{slug}/reviews")]
    public async Task<IActionResult> Reviews(string slug, int page = 1, CancellationToken cancellationToken = default)
    {
        var product = await catalog.GetProductBySlugAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var result = await reviews.GetForProductAsync(product.Id, page, cancellationToken);
        return PartialView("_ReviewList", new ReviewListViewModel(product.Slug, result.Reviews));
    }

    /// <summary>POST /products/{slug}/reviews - create or update the signed-in customer's review (multipart, up to 3 images).</summary>
    [HttpPost("{slug}/reviews")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    [EnableRateLimiting(RateLimitPolicies.Forms)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> SubmitReview(string slug, [Bind(Prefix = "Review")] ReviewCommand command, List<IFormFile>? images, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductBySlugAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var uploads = (images ?? []).Where(f => f.Length > 0).ToList();
        var streams = new List<Stream>();
        try
        {
            ModelState.ThrowIfBindingFailed();
            var files = new List<(Stream Content, string FileName)>();
            foreach (var file in uploads.Take(ReviewService.MaxImages + 1))
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add((stream, file.FileName));
            }

            await reviews.SubmitAsync(User.UserId()!, User.DisplayName(), product.Id, command, files, cancellationToken);
            TempData[ReviewMessageKey] = "Cảm ơn bạn đã đánh giá sản phẩm!";
            return Redirect($"{product.Url}#reviews");
        }
        catch (AppValidationException ex)
        {
            foreach (var (field, messages) in ex.FieldErrors)
            {
                foreach (var message in messages) ModelState.AddModelError($"Review.{field}", message);
            }
            foreach (var message in ex.Errors.Where(e => !ex.FieldErrors.Values.Any(v => v.Contains(e))))
            {
                ModelState.AddModelError(string.Empty, message);
            }
        }
        catch (ForbiddenAccessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }

        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Detail), await BuildDetailAsync(product, null, 1, command, cancellationToken));
    }

    private async Task<ProductDetailViewModel> BuildDetailAsync(ProductDetailDto product, int? variant, int reviewPage, ReviewCommand? postedReview, CancellationToken cancellationToken)
    {
        var related = await catalog.GetRelatedProductsAsync(product, 8, cancellationToken);
        var productReviews = await reviews.GetForProductAsync(product.Id, reviewPage, cancellationToken);
        var eligibility = await reviews.GetEligibilityAsync(User.UserId(), product.Id, cancellationToken);

        var form = postedReview ?? (eligibility.Existing is { } existing
            ? new ReviewCommand { Rating = existing.Rating, Title = existing.Title, Comment = existing.Comment }
            : new ReviewCommand());

        return new ProductDetailViewModel(product, related, variant)
        {
            Reviews = productReviews,
            ReviewEligibility = eligibility,
            ReviewForm = form,
            ShowReviewForm = postedReview is not null
        };
    }
}
