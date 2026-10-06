using System.ComponentModel.DataAnnotations;

namespace FurnitureStore.Application.Common.Settings;

/// <summary>Shipping fee rules, bound from the "Shipping" section.</summary>
public sealed class ShippingSettings
{
    public const string SectionName = "Shipping";

    /// <summary>Orders with a subtotal (after discount) at or above this amount ship for free.</summary>
    [Range(0, 1_000_000_000)]
    public decimal FreeShippingThreshold { get; set; } = 10_000_000;

    /// <summary>Delivery + installation fee for smaller orders.</summary>
    [Range(0, 100_000_000)]
    public decimal StandardFee { get; set; } = 300_000;
}
