using FurnitureStore.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FurnitureStore.Infrastructure.Identity;

/// <summary>
/// Identity user (table AspNetUsers) extended with profile data.
/// Domain entities reference users by <c>UserId</c> only; the navigation collections below exist on the
/// Infrastructure side so EF Core can create the foreign keys without the Domain depending on Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }

    /// <summary>Soft switch used by admins in addition to Identity lockout.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public Cart? Cart { get; set; }
    public Wishlist? Wishlist { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ChatConversation> ChatConversations { get; set; } = new List<ChatConversation>();
    public ICollection<AIConversation> AIConversations { get; set; } = new List<AIConversation>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

/// <summary>Identity role (table AspNetRoles).</summary>
public class ApplicationRole : IdentityRole
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName, string? description = null) : base(roleName)
    {
        Description = description;
    }

    public string? Description { get; set; }
}
