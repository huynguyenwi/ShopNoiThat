namespace FurnitureStore.Domain.Constants;

/// <summary>
/// Role names used by ASP.NET Core Identity and [Authorize(Roles = ...)] checks.
/// </summary>
public static class AppRoles
{
    public const string Admin = "ADMIN";
    public const string User = "USER";

    // Reserved for future expansion.
    public const string Staff = "STAFF";
    public const string Customer = "CUSTOMER";

    /// <summary>Roles allowed into the back-office (Admin area).</summary>
    public const string BackOffice = Admin + "," + Staff;

    public static readonly IReadOnlyList<string> All = [Admin, User, Staff, Customer];
}
