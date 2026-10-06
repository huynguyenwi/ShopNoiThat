using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Tests.Domain;

public sealed class OrderTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Processing)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipping)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Refunded)]
    public void AllowedTransitions_Succeed(OrderStatus from, OrderStatus to)
    {
        Assert.True(OrderStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Refunded, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Pending)]
    public void ForbiddenTransitions_AreRejected(OrderStatus from, OrderStatus to)
    {
        var order = new Order { Status = from };

        Assert.False(OrderStatusTransitions.CanTransition(from, to));
        Assert.Throws<DomainException>(() => order.ChangeStatus(to, Now, "admin"));
    }

    [Fact]
    public void ChangeStatus_StampsTimestamps_AndAppendsHistory()
    {
        var order = new Order { Status = OrderStatus.Pending, PaymentMethod = PaymentMethod.COD };

        order.ChangeStatus(OrderStatus.Confirmed, Now, "admin");
        order.ChangeStatus(OrderStatus.Processing, Now.AddHours(1), "admin");
        order.ChangeStatus(OrderStatus.Shipping, Now.AddDays(1), "admin", "Giao qua đội xe của xưởng");
        order.ChangeStatus(OrderStatus.Delivered, Now.AddDays(2), "admin");

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now, order.ConfirmedAt);
        Assert.Equal(Now.AddDays(1), order.ShippedAt);
        Assert.Equal(Now.AddDays(2), order.DeliveredAt);
        Assert.Equal(4, order.StatusHistory.Count);
        Assert.Equal(OrderStatus.Shipping, order.StatusHistory.ElementAt(2).ToStatus);
        Assert.Equal(OrderStatus.Processing, order.StatusHistory.ElementAt(2).FromStatus);
        // Cash on delivery is paid once delivered.
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public void Cancel_RecordsReason()
    {
        var order = new Order { Status = OrderStatus.Pending };

        order.ChangeStatus(OrderStatus.Cancelled, Now, "customer", "Đổi ý");

        Assert.Equal("Đổi ý", order.CancelReason);
        Assert.Equal(Now, order.CancelledAt);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Processing, false)]
    [InlineData(OrderStatus.Shipping, false)]
    [InlineData(OrderStatus.Delivered, false)]
    public void CustomerCanCancel_OnlyBeforeProcessing(OrderStatus status, bool expected)
    {
        Assert.Equal(expected, new Order { Status = status }.CanBeCancelledByCustomer);
    }

    [Fact]
    public void RecalculateTotals_UsesItemsDiscountAndShipping()
    {
        var order = new Order { DiscountAmount = 500_000, ShippingFee = 300_000 };
        order.Items.Add(new OrderItem { UnitPrice = 9_800_000, Quantity = 1 });
        order.Items.Add(new OrderItem { UnitPrice = 1_850_000, Quantity = 4 });

        order.RecalculateTotals();

        Assert.Equal(17_200_000, order.Subtotal);
        Assert.Equal(17_000_000, order.TotalAmount);
        Assert.Equal(7_400_000, order.Items.Last().LineTotal);
    }

    [Fact]
    public void RecalculateTotals_NeverDiscountsMoreThanSubtotal()
    {
        var order = new Order { DiscountAmount = 5_000_000, ShippingFee = 0 };
        order.Items.Add(new OrderItem { UnitPrice = 1_000_000, Quantity = 1 });

        order.RecalculateTotals();

        Assert.Equal(1_000_000, order.DiscountAmount);
        Assert.Equal(0, order.TotalAmount);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, PaymentStatus.Unpaid, true)]
    [InlineData(OrderStatus.Shipping, PaymentStatus.Unpaid, true)]
    [InlineData(OrderStatus.Confirmed, PaymentStatus.Paid, false)]
    [InlineData(OrderStatus.Delivered, PaymentStatus.Paid, false)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Unpaid, false)]
    [InlineData(OrderStatus.Refunded, PaymentStatus.Refunded, false)]
    public void ShippingFee_CanBeRecorded_UntilTheOrderIsDeliveredCancelledOrPaid(OrderStatus status, PaymentStatus payment, bool expected)
    {
        Assert.Equal(expected, new Order { Status = status, PaymentStatus = payment }.CanChangeShippingFee);
    }

    [Fact]
    public void ChangeShippingFee_AddsItToTheTotal_AndRejectsOutOfRangeAmounts()
    {
        var order = new Order { DiscountAmount = 500_000 };
        order.Items.Add(new OrderItem { UnitPrice = 7_900_000, Quantity = 1 });
        order.RecalculateTotals();

        order.ChangeShippingFee(350_000);

        Assert.Equal(350_000, order.ShippingFee);
        Assert.Equal(7_750_000, order.TotalAmount);
        Assert.Throws<DomainException>(() => order.ChangeShippingFee(-1));
        Assert.Throws<DomainException>(() => order.ChangeShippingFee(Order.MaxShippingFee + 1));

        order.Status = OrderStatus.Cancelled;
        Assert.Throws<DomainException>(() => order.ChangeShippingFee(0));
        Assert.Equal(350_000, order.ShippingFee);
    }
}
