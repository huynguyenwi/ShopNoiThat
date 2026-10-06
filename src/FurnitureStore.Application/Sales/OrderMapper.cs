using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

public static class OrderMapper
{
    public static OrderDetailDto ToDetail(Order order, PaymentInstructionsDto? instructions)
    {
        var address = order.Addresses.FirstOrDefault(a => a.AddressType == AddressType.Shipping);
        return new OrderDetailDto(
            order.Id, order.OrderCode, order.UserId, order.Status, order.PaymentStatus, order.PaymentMethod,
            order.Subtotal, order.DiscountAmount, order.ShippingFee, order.TotalAmount, order.CouponCode,
            order.CustomerName, order.CustomerPhone, order.CustomerEmail, order.CustomerNote, order.AdminNote, order.CancelReason,
            order.PlacedAt, order.ConfirmedAt, order.ShippedAt, order.DeliveredAt, order.CancelledAt, order.Version,
            address is null ? null : new OrderAddressDto(address.RecipientName, address.Phone, address.Email, address.AddressLine, address.Ward, address.District, address.Province),
            order.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto(
                i.Id, i.ProductId, i.ProductName, i.Product?.Slug, i.VariantName, i.Sku, i.ImageUrl,
                i.ColorName, i.MaterialName, i.SizeName, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
            order.StatusHistory.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                .Select(h => new OrderStatusHistoryDto(h.FromStatus, h.ToStatus, h.Note, h.ChangedBy, h.ChangedAt)).ToList(),
            order.Payments.OrderBy(p => p.Id)
                .Select(p => new PaymentDto(p.Id, p.Method, p.Status, p.Amount, p.TransactionCode, p.CreatedAt, p.PaidAt, p.Note)).ToList(),
            instructions);
    }
}
