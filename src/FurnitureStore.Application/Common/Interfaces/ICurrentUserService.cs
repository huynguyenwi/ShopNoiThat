namespace FurnitureStore.Application.Common.Interfaces;

/// <summary>
/// Information about the user making the current request. Implemented in the Web layer from HttpContext.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
}
