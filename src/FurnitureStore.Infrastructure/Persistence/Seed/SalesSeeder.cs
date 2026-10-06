using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>Demo coupons for testing the cart / checkout flow.</summary>
public sealed class SalesSeeder(ApplicationDbContext context, TimeProvider timeProvider, ILogger<SalesSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.Coupons.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        context.Coupons.AddRange(
            new Coupon
            {
                Code = "CHAOBAN10",
                Name = "Giảm 10% cho khách hàng mới",
                Description = "Giảm 10% tối đa 2.000.000đ cho đơn từ 5.000.000đ, mỗi khách dùng 1 lần.",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10,
                MaxDiscountAmount = 2_000_000,
                MinOrderAmount = 5_000_000,
                UsageLimitPerUser = 1,
                StartsAt = now.AddDays(-1),
                IsActive = true,
                IsPublic = true
            },
            new Coupon
            {
                Code = "GIAM500K",
                Name = "Giảm 500.000đ",
                Description = "Giảm 500.000đ cho đơn từ 10.000.000đ. Giới hạn 100 lượt.",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 500_000,
                MinOrderAmount = 10_000_000,
                UsageLimit = 100,
                StartsAt = now.AddDays(-1),
                EndsAt = now.AddMonths(6),
                IsActive = true,
                IsPublic = true
            },
            new Coupon
            {
                Code = "HETHAN",
                Name = "Mã đã hết hạn (để kiểm thử)",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 50,
                StartsAt = now.AddMonths(-3),
                EndsAt = now.AddMonths(-1),
                IsActive = true
            });

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded demo coupons");
    }
}
