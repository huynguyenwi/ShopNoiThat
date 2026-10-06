using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FurnitureStore.Web.Controllers;

public sealed record ContactPageViewModel(StoreInfoDto Store, ContactCommand Command);

/// <summary>/contact - store information, map and contact form.</summary>
[Route("contact")]
public sealed class ContactController(IContactService contacts, IStoreInfoService store, UserManager<ApplicationUser> userManager) : Controller
{
    public const string MessageKey = "ContactMessage";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var command = new ContactCommand();
        if (User.Identity?.IsAuthenticated == true && await userManager.GetUserAsync(User) is { } user)
        {
            command.FullName = user.FullName;
            command.Email = user.Email ?? string.Empty;
            command.Phone = user.PhoneNumber ?? string.Empty;
        }

        return View(new ContactPageViewModel(await store.GetAsync(cancellationToken), command));
    }

    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Forms)]
    public async Task<IActionResult> Index([Bind(Prefix = "Command")] ContactCommand command, string? website, CancellationToken cancellationToken)
    {
        // Honeypot: real visitors never fill the hidden "website" field; bots usually do.
        if (!string.IsNullOrEmpty(website))
        {
            TempData[MessageKey] = "Cảm ơn bạn! Chúng tôi sẽ liên hệ lại sớm.";
            return Redirect("/contact#contact-form");
        }

        try
        {
            ModelState.ThrowIfBindingFailed();
            await contacts.SubmitAsync(command, User.UserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            TempData[MessageKey] = "Cảm ơn bạn! Tin nhắn đã được gửi, chúng tôi sẽ liên hệ lại trong giờ làm việc.";
            return Redirect("/contact#contact-form");
        }
        catch (AppValidationException ex)
        {
            foreach (var (field, messages) in ex.FieldErrors)
            {
                foreach (var message in messages) ModelState.AddModelError($"Command.{field}", message);
            }

            return View(new ContactPageViewModel(await store.GetAsync(cancellationToken), command));
        }
    }
}
