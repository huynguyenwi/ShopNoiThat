using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FurnitureStore.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Before every SaveChanges:
/// - fills CreatedAt/CreatedBy and UpdatedAt/UpdatedBy of <see cref="IAuditableEntity"/>;
/// - fills CreatedAt of any other entity that has such a property and was left unset;
/// - turns deletes of <see cref="ISoftDelete"/> entities into "IsDeleted = true" updates;
/// - regenerates the optimistic concurrency token of <see cref="IConcurrencyAware"/> entities.
/// </summary>
public sealed class AuditableEntityInterceptor(ICurrentUserService currentUser, TimeProvider timeProvider) : SaveChangesInterceptor
{
    private const string SystemUser = "system";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyRules(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ApplyRules(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyRules(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = currentUser.UserName ?? currentUser.UserId ?? SystemUser;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete softDelete)
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = now;
                softDelete.DeletedBy = user;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    SetCreated(entry, now, user);
                    if (entry.Entity is IConcurrencyAware addedVersioned)
                    {
                        addedVersioned.Version = Guid.NewGuid();
                    }
                    break;

                case EntityState.Modified:
                    if (entry.Entity is IAuditableEntity modified)
                    {
                        modified.UpdatedAt = now;
                        modified.UpdatedBy = user;
                    }
                    if (entry.Entity is IConcurrencyAware modifiedVersioned)
                    {
                        modifiedVersioned.Version = Guid.NewGuid();
                    }
                    break;
            }
        }
    }

    private static void SetCreated(EntityEntry entry, DateTime now, string user)
    {
        if (entry.Entity is IAuditableEntity auditable)
        {
            if (auditable.CreatedAt == default)
            {
                auditable.CreatedAt = now;
            }
            auditable.CreatedBy ??= user;
            return;
        }

        // Non-auditable entities that still carry a CreatedAt column (images, notifications, AI messages...).
        var createdAt = entry.Metadata.FindProperty("CreatedAt");
        if (createdAt is not null && createdAt.ClrType == typeof(DateTime)
            && entry.Property(createdAt.Name).CurrentValue is DateTime value && value == default)
        {
            entry.Property(createdAt.Name).CurrentValue = now;
        }
    }
}
