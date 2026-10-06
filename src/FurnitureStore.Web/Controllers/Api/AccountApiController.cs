using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

public sealed record CurrentUserDto(string Id, string Email, string FullName, string? PhoneNumber, string? AvatarUrl, IReadOnlyList<string> Roles);

[Route("api/account")]
[Authorize]
public sealed class AccountApiController(UserManager<ApplicationUser> userManager) : ApiControllerBase
{
    /// <summary>GET /api/account/me - the signed-in user (401 JSON when anonymous).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User) ?? throw new NotFoundException("Không tìm thấy tài khoản.");
        var roles = await userManager.GetRolesAsync(user);

        return OkResponse(new CurrentUserDto(user.Id, user.Email ?? string.Empty, user.FullName, user.PhoneNumber, user.AvatarUrl, roles.ToList()));
    }
}
