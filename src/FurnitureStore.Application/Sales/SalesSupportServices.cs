using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.Sales;

public sealed class ShippingCalculator(IOptions<ShippingSettings> options) : IShippingCalculator
{
    public decimal FreeShippingThreshold => options.Value.FreeShippingThreshold;

    public decimal Calculate(decimal subtotalAfterDiscount, string? province = null) =>
        subtotalAfterDiscount >= options.Value.FreeShippingThreshold ? 0 : options.Value.StandardFee;
}

/// <summary>Chooses the provider for an order's payment method among the methods enabled in configuration.</summary>
public sealed class PaymentService(IEnumerable<IPaymentProvider> providers, IOptions<PaymentSettings> options) : IPaymentService
{
    public IReadOnlyList<PaymentMethodOption> GetAvailableMethods() =>
        providers.Where(p => IsEnabled(p.Method))
            .OrderBy(p => p.Method)
            .Select(p => new PaymentMethodOption(p.Method, p.DisplayName, p.Description))
            .ToList();

    public bool IsEnabled(PaymentMethod method) =>
        providers.Any(p => p.Method == method)
        && options.Value.EnabledMethods.Contains(method.ToString(), StringComparer.OrdinalIgnoreCase);

    public Task<PaymentInitResult> InitiateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(p => p.Method == order.PaymentMethod && IsEnabled(p.Method))
                       ?? throw new Common.Exceptions.BusinessRuleException("Phương thức thanh toán không khả dụng.");
        return provider.InitiateAsync(order, cancellationToken);
    }

    public PaymentInstructionsDto? GetInstructions(Order order)
    {
        if (order.PaymentMethod != PaymentMethod.BankTransfer)
        {
            return null;
        }

        var bank = options.Value.BankTransfer;
        return new PaymentInstructionsDto(bank.BankName, bank.AccountNumber, bank.AccountName, bank.Branch,
            $"{bank.TransferNotePrefix} {order.OrderCode}", order.TotalAmount);
    }
}

public sealed class NotificationService(IRepository<Notification> notifications, IUnitOfWork unitOfWork, TimeProvider timeProvider) : INotificationService
{
    public Task NotifyRoleAsync(string role, NotificationType type, string title, string message, string? link, CancellationToken cancellationToken = default) =>
        AddAsync(new Notification { RecipientRole = role, Type = type, Title = title, Message = message, Link = link }, cancellationToken);

    public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? link, CancellationToken cancellationToken = default) =>
        AddAsync(new Notification { UserId = userId, Type = type, Title = title, Message = message, Link = link }, cancellationToken);

    private async Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        notification.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        notification.Title = Truncate(notification.Title, 200);
        notification.Message = Truncate(notification.Message, 1000);
        await notifications.AddAsync(notification, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
