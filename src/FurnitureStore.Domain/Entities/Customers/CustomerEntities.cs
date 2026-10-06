using FurnitureStore.Domain.Common;

namespace FurnitureStore.Domain.Entities;

/// <summary>Saved delivery address of a customer (address book).</summary>
public class CustomerAddress : AuditableEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Label shown in the address book, e.g. "Nhà riêng", "Công ty".</summary>
    public string? Label { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string? District { get; set; }
    public string Province { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class Wishlist : AuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public ICollection<WishlistItem> Items { get; set; } = new List<WishlistItem>();

    /// <summary>Adds the product if missing. Returns false when it was already there.</summary>
    public bool Add(int productId, DateTime utcNow)
    {
        if (Items.Any(i => i.ProductId == productId))
        {
            return false;
        }

        Items.Add(new WishlistItem { ProductId = productId, AddedAt = utcNow });
        return true;
    }
}

public class WishlistItem : BaseEntity
{
    public int WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime AddedAt { get; set; }
}

/// <summary>Product review. Only customers who bought and received the product may review it.</summary>
public class Review : AuditableEntity
{
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name snapshot of the reviewer.</summary>
    public string ReviewerName { get; set; } = string.Empty;

    /// <summary>Order line that proves the purchase.</summary>
    public int? OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }

    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }

    /// <summary>Hidden by an admin (violates policy); hidden reviews are excluded from ratings.</summary>
    public bool IsHidden { get; set; }

    public string? AdminReply { get; set; }
    public DateTime? AdminRepliedAt { get; set; }

    public ICollection<ReviewImage> Images { get; set; } = new List<ReviewImage>();
}

public class ReviewImage : BaseEntity
{
    public int ReviewId { get; set; }
    public Review Review { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
