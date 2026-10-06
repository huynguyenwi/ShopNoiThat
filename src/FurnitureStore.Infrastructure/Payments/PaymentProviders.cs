using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Payments;

/// <summary>Cash on delivery: nothing to charge now; the payment becomes Paid when the order is delivered.</summary>
public sealed class CodPaymentProvider : IPaymentProvider
{
    public PaymentMethod Method => PaymentMethod.COD;
    public string DisplayName => "Thanh toán khi nhận hàng (COD)";
    public string Description => "Thanh toán tiền mặt hoặc chuyển khoản cho nhân viên khi nhận và kiểm tra hàng.";

    public Task<PaymentInitResult> InitiateAsync(Order order, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentInitResult(PaymentStatus.Pending, null, "Thu tiền khi giao hàng", null));
}

/// <summary>
/// Simulated bank transfer: the customer transfers with the order code in the note and an admin confirms
/// the payment when the money arrives. A bank / VietQR webhook can replace the manual confirmation later.
/// </summary>
public sealed class BankTransferPaymentProvider(IOptions<PaymentSettings> options) : IPaymentProvider
{
    public PaymentMethod Method => PaymentMethod.BankTransfer;
    public string DisplayName => "Chuyển khoản ngân hàng";
    public string Description => "Chuyển khoản theo thông tin hiển thị sau khi đặt hàng. Đơn hàng được xử lý khi cửa hàng nhận được tiền.";

    public Task<PaymentInitResult> InitiateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var bank = options.Value.BankTransfer;
        var note = $"{bank.TransferNotePrefix} {order.OrderCode}";
        var instructions = new PaymentInstructionsDto(bank.BankName, bank.AccountNumber, bank.AccountName, bank.Branch, note, order.TotalAmount);
        return Task.FromResult(new PaymentInitResult(PaymentStatus.Pending, note, "Chờ khách chuyển khoản", instructions));
    }
}
