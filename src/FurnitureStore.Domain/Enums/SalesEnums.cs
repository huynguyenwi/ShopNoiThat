namespace FurnitureStore.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipping = 3,
    Delivered = 4,
    Cancelled = 5,
    Refunded = 6
}

public enum PaymentMethod
{
    COD = 1,
    BankTransfer = 2,
    VNPay = 3,
    MoMo = 4
}

public enum PaymentStatus
{
    Unpaid = 0,
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2
}

/// <summary>Where a coupon stands at a given moment (computed, not stored).</summary>
public enum CouponStatus
{
    Running = 1,
    Scheduled = 2,
    Expired = 3,
    Exhausted = 4,
    Inactive = 5
}

public enum AddressType
{
    Shipping = 1,
    Billing = 2
}
