using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

/// <summary>/admin/coupons - discount codes: create, edit, switch on / off, delete unused ones, usage statistics.</summary>
public interface ICouponAdminService
{
    Task<PagedResult<CouponListItemDto>> SearchAsync(CouponQuery query, CancellationToken cancellationToken = default);
    Task<CouponDetailDto> GetDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<CouponCommand> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(CouponCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, CouponCommand command, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class CouponAdminService(
    ICouponRepository coupons,
    IValidator<CouponCommand> validator,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLog,
    TimeProvider timeProvider) : ICouponAdminService
{
    public const int RecentOrdersShown = 50;

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public Task<PagedResult<CouponListItemDto>> SearchAsync(CouponQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        return coupons.SearchAsync(query, UtcNow, cancellationToken);
    }

    public async Task<CouponDetailDto> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await coupons.GetListItemAsync(id, UtcNow, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);
        var coupon = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);
        var (orderCount, revenue) = await coupons.GetOrderStatsAsync(id, cancellationToken);
        var recent = await coupons.GetRecentOrdersAsync(id, RecentOrdersShown, cancellationToken);
        return new CouponDetailDto(item, coupon.Description, orderCount, revenue, recent, coupon.CreatedAt, coupon.CreatedBy);
    }

    public async Task<CouponCommand> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var c = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);
        return new CouponCommand
        {
            Code = c.Code, Name = c.Name, Description = c.Description, DiscountType = c.DiscountType, DiscountValue = c.DiscountValue,
            MaxDiscountAmount = c.MaxDiscountAmount, MinOrderAmount = c.MinOrderAmount == 0 ? null : c.MinOrderAmount,
            StartsAt = c.StartsAt is DateTime starts ? VietnamTime.ToLocal(starts) : null,
            EndsAt = c.EndsAt is DateTime ends ? VietnamTime.ToLocal(ends) : null,
            UsageLimit = c.UsageLimit, UsageLimitPerUser = c.UsageLimitPerUser, IsActive = c.IsActive, IsPublic = c.IsPublic
        };
    }

    public async Task<int> CreateAsync(CouponCommand command, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(null, command, cancellationToken);
        if (command.EndsAt is DateTime ends && VietnamTime.ToUtc(ends) <= UtcNow)
        {
            throw new AppValidationException(nameof(CouponCommand.EndsAt), "Thời gian kết thúc đã qua.");
        }

        var coupon = new Coupon();
        Apply(coupon, command);
        await coupons.AddAsync(coupon, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, nameof(Coupon), coupon.Id.ToString(),
            $"Tạo mã giảm giá {coupon.Code}", NewValues: Snapshot(coupon)), cancellationToken);
        return coupon.Id;
    }

    public async Task UpdateAsync(int id, CouponCommand command, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(id, command, cancellationToken);
        var coupon = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);

        var (orderCount, _) = await coupons.GetOrderStatsAsync(id, cancellationToken);
        var codeChanged = coupon.Code != command.Code;
        if (codeChanged && orderCount > 0)
        {
            throw new AppValidationException(nameof(CouponCommand.Code),
                $"Mã đã được dùng cho {orderCount} đơn hàng nên không thể đổi. Hãy tạo mã mới nếu cần.");
        }

        // Checked against the latest count: customers keep using the code while the form is open.
        if (command.UsageLimit is int limit && limit < coupon.UsedCount)
        {
            throw new AppValidationException(nameof(CouponCommand.UsageLimit),
                $"Tổng số lượt không được nhỏ hơn số lượt đã dùng ({coupon.UsedCount}).");
        }

        var before = Snapshot(coupon);
        var oldCode = coupon.Code;
        Apply(coupon, command);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (codeChanged)
        {
            await coupons.ReplaceCartCouponCodeAsync(oldCode, coupon.Code, cancellationToken);
        }

        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Coupon), id.ToString(),
            $"Sửa mã giảm giá {coupon.Code}", OldValues: before, NewValues: Snapshot(coupon)), cancellationToken);
    }

    public async Task SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);
        coupon.IsActive = active;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Coupon), id.ToString(),
            $"{(active ? "Bật" : "Tắt")} mã giảm giá {coupon.Code}"), cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("mã giảm giá", id);
        var (orderCount, _) = await coupons.GetOrderStatsAsync(id, cancellationToken);
        if (orderCount > 0)
        {
            throw new BusinessRuleException(
                $"Mã {coupon.Code} đã được dùng cho {orderCount} đơn hàng nên không thể xóa (cần giữ lịch sử). Hãy tắt mã để ngừng áp dụng.");
        }

        coupons.Remove(coupon);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await coupons.ReplaceCartCouponCodeAsync(coupon.Code, null, cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(Coupon), id.ToString(),
            $"Xóa mã giảm giá {coupon.Code}", OldValues: Snapshot(coupon)), cancellationToken);
    }

    private async Task ValidateAsync(int? id, CouponCommand command, CancellationToken cancellationToken)
    {
        command.Code = Coupon.NormalizeCode(command.Code);
        command.Name = (command.Name ?? string.Empty).Trim();
        command.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        if (command.DiscountType == DiscountType.FixedAmount)
        {
            command.MaxDiscountAmount = null; // a fixed amount is its own cap
        }

        await validator.EnsureValidAsync(command, cancellationToken);
        if (await coupons.CodeExistsAsync(command.Code, id, cancellationToken))
        {
            throw new AppValidationException(nameof(CouponCommand.Code), "Mã này đã tồn tại.");
        }
    }

    private static void Apply(Coupon coupon, CouponCommand command)
    {
        coupon.Code = command.Code;
        coupon.Name = command.Name;
        coupon.Description = command.Description;
        coupon.DiscountType = command.DiscountType;
        coupon.DiscountValue = command.DiscountValue;
        coupon.MaxDiscountAmount = command.MaxDiscountAmount;
        coupon.MinOrderAmount = command.MinOrderAmount ?? 0;
        coupon.StartsAt = command.StartsAt is DateTime starts ? VietnamTime.ToUtc(starts) : null;
        coupon.EndsAt = command.EndsAt is DateTime ends ? VietnamTime.ToUtc(ends) : null;
        coupon.UsageLimit = command.UsageLimit;
        coupon.UsageLimitPerUser = command.UsageLimitPerUser;
        coupon.IsActive = command.IsActive;
        coupon.IsPublic = command.IsPublic;
    }

    private static object Snapshot(Coupon c) => new
    {
        c.Code, c.DiscountType, c.DiscountValue, c.MaxDiscountAmount, c.MinOrderAmount, c.StartsAt, c.EndsAt,
        c.UsageLimit, c.UsageLimitPerUser, c.IsActive, c.IsPublic
    };
}
