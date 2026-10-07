using System.Security.Cryptography;
using FluentValidation;
using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Application.Qr;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.Sales;

public interface IOrderService
{
    Task<OrderPlacedResult> PlaceOrderAsync(string userId, CheckoutCommand command, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderListItemDto>> GetMyOrdersAsync(string userId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<OrderDetailDto> GetMyOrderAsync(string userId, string orderCode, CancellationToken cancellationToken = default);
    Task<OrderDetailDto> GetMyOrderAsync(string userId, int orderId, CancellationToken cancellationToken = default);
    Task CancelMyOrderAsync(string userId, string orderCode, string? reason, CancellationToken cancellationToken = default);
}

public sealed class OrderService(
    ICartRepository carts,
    IOrderRepository orders,
    ICouponRepository coupons,
    IRepository<CouponUsage> couponUsages,
    IRepository<CustomerAddress> addresses,
    IInventoryRepository inventory,
    IPaymentService payments,
    INotificationService notifications,
    OrderWorkflow workflow,
    IValidator<CheckoutCommand> validator,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    IOptions<ApplicationSettings> siteOptions,
    Engagement.IStoreInfoService storeInfo,
    TimeProvider timeProvider,
    ILogger<OrderService> logger) : IOrderService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<OrderPlacedResult> PlaceOrderAsync(string userId, CheckoutCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        if (!payments.IsEnabled(command.PaymentMethod))
        {
            throw new AppValidationException(nameof(command.PaymentMethod), "Phương thức thanh toán không khả dụng.");
        }

        var owner = new CartOwner(userId, null);
        var order = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var cart = await carts.GetAsync(owner, ct);
            var allLines = cart is null ? [] : await carts.GetLinesAsync(cart.Id, ct);
            if (cart is null || allLines.Count == 0)
            {
                throw new BusinessRuleException("Giỏ hàng của bạn đang trống.");
            }

            // Only the ticked lines are ordered; the others stay in the cart.
            var lines = allLines.Where(l => l.IsSelected).ToList();
            if (lines.Count == 0)
            {
                throw new BusinessRuleException("Vui lòng tích chọn sản phẩm muốn đặt trong giỏ hàng.");
            }

            var unavailable = lines.FirstOrDefault(l => !l.IsAvailable);
            if (unavailable is not null)
            {
                throw new BusinessRuleException($"\"{unavailable.ProductName}\" không còn bán. Vui lòng xóa khỏi giỏ hàng.");
            }

            // Reserve stock atomically; a concurrent buyer of the last item makes this fail cleanly.
            foreach (var line in lines)
            {
                if (!await inventory.TryReserveAsync(line.VariantId, line.Quantity, ct))
                {
                    throw new BusinessRuleException($"\"{line.ProductName} - {line.VariantName}\" không đủ hàng (còn {line.Stock}). Vui lòng cập nhật giỏ hàng.");
                }

                await inventory.AdjustProductAsync(line.ProductId, stockDelta: -line.Quantity, soldDelta: line.Quantity, ct);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var subtotal = lines.Sum(l => l.LineTotal);

            Coupon? coupon = null;
            decimal discount = 0;
            if (!string.IsNullOrEmpty(cart.CouponCode))
            {
                coupon = await coupons.GetByCodeAsync(cart.CouponCode, ct);
                var reason = coupon is null
                    ? "Mã giảm giá không còn tồn tại."
                    : coupon.GetInvalidReason(subtotal, now, await coupons.CountUsageByUserAsync(coupon.Id, userId, ct));
                if (reason is not null)
                {
                    throw new BusinessRuleException($"{reason} Vui lòng bỏ mã giảm giá để tiếp tục.");
                }

                discount = coupon!.CalculateDiscount(subtotal);
                if (!await coupons.TryConsumeAsync(coupon.Id, ct))
                {
                    throw new BusinessRuleException("Mã giảm giá vừa hết lượt sử dụng. Vui lòng bỏ mã để tiếp tục.");
                }
            }

            var newOrder = new Order
            {
                OrderCode = await GenerateOrderCodeAsync(now, ct),
                UserId = userId,
                Status = OrderStatus.Pending,
                PaymentMethod = command.PaymentMethod,
                PaymentStatus = PaymentStatus.Unpaid,
                CouponId = coupon?.Id,
                CouponCode = coupon?.Code,
                DiscountAmount = discount,
                CustomerName = command.FullName.Trim(),
                CustomerPhone = command.Phone.Trim(),
                CustomerEmail = command.Email.Trim(),
                CustomerNote = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
                PlacedAt = now
            };

            foreach (var line in lines)
            {
                newOrder.Items.Add(new OrderItem
                {
                    ProductId = line.ProductId,
                    ProductVariantId = line.VariantId,
                    ProductName = line.ProductName,
                    VariantName = line.VariantName,
                    Sku = line.Sku,
                    ImageUrl = line.ImageUrl,
                    ColorName = line.ColorName,
                    MaterialName = line.MaterialName,
                    SizeName = line.SizeName,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Quantity
                });
            }

            newOrder.RecalculateTotals();
            newOrder.Addresses.Add(new OrderAddress
            {
                AddressType = AddressType.Shipping,
                RecipientName = newOrder.CustomerName,
                Phone = newOrder.CustomerPhone,
                Email = newOrder.CustomerEmail,
                AddressLine = command.AddressLine.Trim(),
                Ward = command.Ward.Trim(),
                Province = command.Province.Trim()
            });
            newOrder.StatusHistory.Add(new OrderStatusHistory
            {
                ToStatus = OrderStatus.Pending,
                Note = "Khách hàng đặt hàng",
                ChangedBy = currentUser.UserName ?? newOrder.CustomerEmail,
                ChangedAt = now
            });

            var payment = await payments.InitiateAsync(newOrder, ct);
            newOrder.PaymentStatus = payment.Status == PaymentStatus.Pending && newOrder.PaymentMethod != PaymentMethod.COD
                ? PaymentStatus.Pending
                : PaymentStatus.Unpaid;
            newOrder.Payments.Add(new Payment
            {
                Method = newOrder.PaymentMethod,
                Status = payment.Status,
                Amount = newOrder.TotalAmount,
                Provider = newOrder.PaymentMethod.ToString(),
                TransactionCode = payment.TransactionCode,
                Note = payment.Note
            });

            await orders.AddAsync(newOrder, ct);
            if (coupon is not null)
            {
                await couponUsages.AddAsync(new CouponUsage { Coupon = coupon, Order = newOrder, UserId = userId, DiscountAmount = discount, UsedAt = now }, ct);
            }

            cart.RemoveOrdered(lines.Select(l => l.ItemId).ToList());
            if (command.SaveAddress)
            {
                await SaveAddressAsync(userId, command, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.OrderPlaced,
                $"Đơn hàng mới {newOrder.OrderCode}",
                $"{newOrder.CustomerName} ({newOrder.CustomerPhone}) đặt {newOrder.TotalQuantity} sản phẩm, tiền hàng {newOrder.TotalAmount:#,0}đ. Gọi lại xác nhận và báo phí giao hàng.",
                $"/admin/orders/details/{newOrder.Id}", ct);

            return newOrder;
        }, cancellationToken);

        logger.LogInformation("Order {OrderCode} placed by user {UserId}: {Total} VND via {PaymentMethod}",
            order.OrderCode, userId, order.TotalAmount, order.PaymentMethod);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, nameof(Order), order.Id.ToString(), $"Đặt hàng {order.OrderCode}",
            NewValues: new { order.OrderCode, order.TotalAmount, order.PaymentMethod, Items = order.Items.Count }), cancellationToken);

        var instructions = payments.GetInstructions(order);
        await TrySendConfirmationAsync(order, instructions);
        return new OrderPlacedResult(order.Id, order.OrderCode, order.TotalAmount, order.PaymentMethod, instructions);
    }

    public Task<PagedResult<OrderListItemDto>> GetMyOrdersAsync(string userId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
        orders.ListForUserAsync(userId, Math.Max(1, page), Math.Clamp(pageSize, 1, 50), cancellationToken);

    public async Task<OrderDetailDto> GetMyOrderAsync(string userId, string orderCode, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetFullByCodeAsync(orderCode, cancellationToken);
        return ToOwnedDetail(order, userId, orderCode);
    }

    public async Task<OrderDetailDto> GetMyOrderAsync(string userId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetFullAsync(orderId, cancellationToken);
        return ToOwnedDetail(order, userId, orderId);
    }

    public async Task CancelMyOrderAsync(string userId, string orderCode, string? reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Khách hàng hủy đơn" : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];

        var order = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await orders.GetFullByCodeAsync(orderCode, ct);
            if (existing is null || existing.UserId != userId)
            {
                throw new NotFoundException("đơn hàng", orderCode);
            }

            if (!existing.CanBeCancelledByCustomer)
            {
                throw new BusinessRuleException("Đơn hàng đang được xử lý hoặc đã giao nên không thể hủy. Vui lòng liên hệ cửa hàng.");
            }

            await workflow.ChangeStatusAsync(existing, OrderStatus.Cancelled, currentUser.UserName ?? existing.CustomerEmail, cleanReason, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.OrderStatusChanged,
                $"Khách hủy đơn {existing.OrderCode}", cleanReason, $"/admin/orders/details/{existing.Id}", ct);
            return existing;
        }, cancellationToken);

        logger.LogInformation("Order {OrderCode} cancelled by customer {UserId}", order.OrderCode, userId);
        await auditLog.LogAsync(new AuditEntry(AuditAction.StatusChange, nameof(Order), order.Id.ToString(), $"Khách hủy đơn {order.OrderCode}: {cleanReason}"), cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private OrderDetailDto ToOwnedDetail(Order? order, string userId, object key)
    {
        // Someone else's order is reported as "not found" so order codes cannot be probed.
        if (order is null || order.UserId != userId)
        {
            throw new NotFoundException("đơn hàng", key);
        }

        return OrderMapper.ToDetail(order, payments.GetInstructions(order));
    }

    private async Task<string> GenerateOrderCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = string.Create(5, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
                }
            });
            var code = $"DH{now:yyMMdd}-{suffix}";
            if (!await orders.CodeExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order code.");
    }

    private async Task SaveAddressAsync(string userId, CheckoutCommand command, CancellationToken cancellationToken)
    {
        var existing = await addresses.ListAsync(a => a.UserId == userId, cancellationToken);
        var duplicate = existing.Any(a =>
            string.Equals(a.AddressLine, command.AddressLine.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Ward, command.Ward.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Province, command.Province.Trim(), StringComparison.OrdinalIgnoreCase)
            && a.Phone == command.Phone.Trim());
        if (duplicate || existing.Count >= AddressService.MaxAddressesPerUser)
        {
            return;
        }

        await addresses.AddAsync(new CustomerAddress
        {
            UserId = userId,
            Label = existing.Count == 0 ? "Nhà riêng" : null,
            RecipientName = command.FullName.Trim(),
            Phone = command.Phone.Trim(),
            AddressLine = command.AddressLine.Trim(),
            Ward = command.Ward.Trim(),
            Province = command.Province.Trim(),
            IsDefault = existing.Count == 0
        }, cancellationToken);
    }

    private async Task TrySendConfirmationAsync(Order order, PaymentInstructionsDto? instructions)
    {
        try
        {
            var site = siteOptions.Value;
            var baseUrl = site.BaseUrl.TrimEnd('/');
            var storeName = (await storeInfo.GetAsync()).Name;
            var html = EmailTemplates.OrderPlaced(storeName, order, instructions, $"{baseUrl}/account/orders/{order.OrderCode}",
                $"{baseUrl}{QrLinks.OrderImage(order.OrderCode, "png")}?scale=5");
            await emailSender.SendAsync(new EmailMessage(order.CustomerEmail, $"Đã nhận yêu cầu đặt hàng {order.OrderCode}", html, order.CustomerName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send confirmation email for order {OrderCode}", order.OrderCode);
        }
    }
}
