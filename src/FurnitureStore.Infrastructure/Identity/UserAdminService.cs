using FurnitureStore.Application.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Identity;

/// <summary>Customer / staff management for admins: search, lock / unlock, role assignment.</summary>
public sealed class UserAdminService(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    IAuditLogService auditLog,
    TimeProvider timeProvider,
    ILogger<UserAdminService> logger) : IUserAdminService
{
    /// <summary>Roles an admin may grant or revoke from the back-office.</summary>
    public static readonly string[] ManageableRoles = [AppRoles.Staff, AppRoles.Admin, AppRoles.User];

    public async Task<PagedResult<AdminUserListItemDto>> ListAsync(AdminUserQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var now = timeProvider.GetUtcNow();
        var users = context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u => u.Email!.Contains(term) || u.FullName.Contains(term) || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim().ToUpperInvariant();
            users = users.Where(u => context.UserRoles.Any(ur => ur.UserId == u.Id && context.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == role)));
        }

        // "Locked" = locked by an admin (IsActive = false). Temporary lockouts after failed logins are shown per row.
        users = query.Status switch
        {
            UserStatusFilter.Active => users.Where(u => u.IsActive),
            UserStatusFilter.Locked => users.Where(u => !u.IsActive),
            _ => users
        };

        var total = await users.CountAsync(cancellationToken);
        var rows = await users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FullName, u.PhoneNumber, u.IsActive, u.LockoutEnd, u.CreatedAt, u.LastLoginAt,
                Roles = context.UserRoles.Where(ur => ur.UserId == u.Id).Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!).ToList(),
                OrderCount = context.Orders.Count(o => o.UserId == u.Id)
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var spent = (await context.Orders.AsNoTracking()
                .Where(o => ids.Contains(o.UserId) && o.Status == OrderStatus.Delivered)
                .Select(o => new { o.UserId, o.TotalAmount })
                .ToListAsync(cancellationToken))
            .GroupBy(o => o.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.TotalAmount));

        var items = rows.Select(r => new AdminUserListItemDto(
            r.Id, r.Email ?? string.Empty, r.FullName, r.PhoneNumber, r.Roles, r.IsActive, r.LockoutEnd > now,
            r.CreatedAt, r.LastLoginAt, r.OrderCount, spent.GetValueOrDefault(r.Id))).ToList();

        return new PagedResult<AdminUserListItemDto>(items, total, page, pageSize);
    }

    public async Task<AdminUserDetailDto> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("người dùng", userId);
        var roles = await userManager.GetRolesAsync(user);
        var now = timeProvider.GetUtcNow();

        var orders = await context.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new AdminOrderListItemDto(o.Id, o.OrderCode, o.PlacedAt, o.CustomerName, o.CustomerPhone, o.Status, o.PaymentStatus,
                o.PaymentMethod, o.TotalAmount, o.Items.Sum(i => i.Quantity)))
            .ToListAsync(cancellationToken);

        var addresses = await context.CustomerAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .Select(a => new CustomerAddressDto(a.Id, a.Label, a.RecipientName, a.Phone, a.AddressLine, a.Ward, a.District, a.Province, a.IsDefault))
            .ToListAsync(cancellationToken);

        var summary = new AdminUserListItemDto(user.Id, user.Email ?? string.Empty, user.FullName, user.PhoneNumber, roles.ToList(),
            user.IsActive, user.LockoutEnd > now, user.CreatedAt, user.LastLoginAt, orders.Count,
            orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.TotalAmount));

        return new AdminUserDetailDto(summary, user.Gender, user.DateOfBirth, user.AvatarUrl, user.AccessFailedCount, addresses, orders.Take(20).ToList());
    }

    public async Task LockAsync(string userId, string? reason, CancellationToken cancellationToken = default)
    {
        EnsureNotSelf(userId, "khóa");
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("người dùng", userId);

        user.IsActive = false;
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        // Rotating the security stamp signs the user out everywhere at the next cookie validation.
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.UpdateAsync(user);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Lock, "User", userId, $"Khóa tài khoản {user.Email}: {reason}"), cancellationToken);
        logger.LogInformation("User {UserId} locked by {Admin}", userId, currentUser.UserName);
    }

    public async Task UnlockAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("người dùng", userId);

        user.IsActive = true;
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.UpdateAsync(user);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Unlock, "User", userId, $"Mở khóa tài khoản {user.Email}"), cancellationToken);
        logger.LogInformation("User {UserId} unlocked by {Admin}", userId, currentUser.UserName);
    }

    public async Task SetRoleAsync(string userId, string role, bool enabled, CancellationToken cancellationToken = default)
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        if (!ManageableRoles.Contains(normalizedRole))
        {
            throw new AppValidationException("Vai trò không hợp lệ.");
        }

        if (normalizedRole == AppRoles.Admin && !enabled)
        {
            EnsureNotSelf(userId, "gỡ quyền quản trị của");
        }

        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("người dùng", userId);
        var hasRole = await userManager.IsInRoleAsync(user, normalizedRole);
        if (hasRole == enabled)
        {
            return;
        }

        if (!enabled && normalizedRole == AppRoles.Admin && (await userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count <= 1)
        {
            throw new BusinessRuleException("Hệ thống phải còn ít nhất một quản trị viên.");
        }

        var result = enabled ? await userManager.AddToRoleAsync(user, normalizedRole) : await userManager.RemoveFromRoleAsync(user, normalizedRole);
        if (!result.Succeeded)
        {
            throw new AppValidationException(result.Errors.Select(e => e.Description));
        }

        await userManager.UpdateSecurityStampAsync(user);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, "UserRole", userId,
            $"{(enabled ? "Cấp" : "Gỡ")} vai trò {normalizedRole} cho {user.Email}"), cancellationToken);
    }

    private void EnsureNotSelf(string userId, string action)
    {
        if (string.Equals(userId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException($"Bạn không thể {action} chính tài khoản của mình.");
        }
    }
}
