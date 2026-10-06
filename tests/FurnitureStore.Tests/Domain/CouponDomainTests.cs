using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Tests.Domain;

public sealed class CouponDomainTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, null, null, null, 0, CouponStatus.Running)]
    [InlineData(true, 1, null, null, 0, CouponStatus.Scheduled)]
    [InlineData(true, null, -1, null, 0, CouponStatus.Expired)]
    [InlineData(true, null, null, 3, 3, CouponStatus.Exhausted)]
    [InlineData(false, null, null, null, 0, CouponStatus.Inactive)]
    [InlineData(false, null, -1, 1, 1, CouponStatus.Inactive)]   // switched off wins
    [InlineData(true, null, -1, 1, 1, CouponStatus.Expired)]     // then expiry
    [InlineData(true, 1, null, 1, 1, CouponStatus.Exhausted)]    // then usage, before "not started"
    public void GetStatus_FollowsPrecedence(bool active, int? startsInDays, int? endsInDays, int? limit, int used, CouponStatus expected)
    {
        var coupon = new Coupon
        {
            IsActive = active,
            StartsAt = startsInDays is int s ? Now.AddDays(s) : null,
            EndsAt = endsInDays is int e ? Now.AddDays(e) : null,
            UsageLimit = limit,
            UsedCount = used
        };

        Assert.Equal(expected, coupon.GetStatus(Now));
    }

    [Fact]
    public void MinimumOrderMessage_UsesVietnameseMoneyFormat()
    {
        var coupon = new Coupon { IsActive = true, MinOrderAmount = 5_000_000 };

        Assert.Equal("Đơn hàng tối thiểu 5.000.000₫ để áp dụng mã này.", coupon.GetInvalidReason(1_000_000, Now, 0));
    }

    [Theory]
    [InlineData("  giam10 ", "GIAM10")]
    [InlineData(null, "")]
    public void NormalizeCode_TrimsAndUpperCases(string? input, string expected) => Assert.Equal(expected, Coupon.NormalizeCode(input));
}
