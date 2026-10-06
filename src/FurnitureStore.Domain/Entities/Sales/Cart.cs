using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// Shopping cart. Belongs to a signed-in user (<see cref="UserId"/>) or to an anonymous visitor
/// identified by a cookie (<see cref="AnonymousId"/>); the anonymous cart is merged on sign-in.
/// </summary>
public class Cart : AuditableEntity
{
    public const int MaxQuantityPerItem = 99;

    public string? UserId { get; set; }
    public string? AnonymousId { get; set; }
    public string? CouponCode { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    public int TotalQuantity => Items.Sum(i => i.Quantity);

    public CartItem AddItem(int productVariantId, int quantity, DateTime utcNow)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Số lượng phải lớn hơn 0.");
        }

        var existing = Items.FirstOrDefault(i => i.ProductVariantId == productVariantId);
        if (existing is not null)
        {
            existing.Quantity = Math.Min(existing.Quantity + quantity, MaxQuantityPerItem);
            return existing;
        }

        var item = new CartItem
        {
            ProductVariantId = productVariantId,
            Quantity = Math.Min(quantity, MaxQuantityPerItem),
            AddedAt = utcNow
        };
        Items.Add(item);
        return item;
    }

    public void SetQuantity(int productVariantId, int quantity)
    {
        var item = Items.FirstOrDefault(i => i.ProductVariantId == productVariantId)
                   ?? throw new DomainException("Sản phẩm không có trong giỏ hàng.");

        if (quantity <= 0)
        {
            Items.Remove(item);
            return;
        }

        item.Quantity = Math.Min(quantity, MaxQuantityPerItem);
    }

    public bool RemoveItem(int productVariantId)
    {
        var item = Items.FirstOrDefault(i => i.ProductVariantId == productVariantId);
        return item is not null && Items.Remove(item);
    }

    /// <summary>Moves every item of <paramref name="other"/> into this cart (used when a guest signs in).</summary>
    public void MergeFrom(Cart other, DateTime utcNow)
    {
        foreach (var item in other.Items)
        {
            AddItem(item.ProductVariantId, item.Quantity, utcNow);
        }

        CouponCode ??= other.CouponCode;
    }

    public void Clear()
    {
        Items.Clear();
        CouponCode = null;
    }
}

public class CartItem : BaseEntity
{
    public int CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; }
}
