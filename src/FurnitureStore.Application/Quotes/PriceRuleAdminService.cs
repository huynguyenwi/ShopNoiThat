using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Quotes;

public sealed class PriceRuleCommandValidator : AbstractValidator<PriceRuleCommand>
{
    public PriceRuleCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Vui lòng nhập mã.").MaximumLength(60)
            .Matches("^[A-Z0-9_\\-]+$").WithMessage("Mã chỉ gồm chữ in hoa, số, gạch dưới hoặc gạch ngang.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên.").MaximumLength(200);
        RuleFor(x => x.RuleType).IsInEnum().WithMessage("Loại tham số không hợp lệ.");
        RuleFor(x => x.FurnitureType).IsInEnum().When(x => x.FurnitureType.HasValue);
        RuleFor(x => x.FinishType).IsInEnum().When(x => x.FinishType.HasValue);
        RuleFor(x => x.Unit).NotEmpty().WithMessage("Vui lòng nhập đơn vị.").MaximumLength(30);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Priority).InclusiveBetween(0, 1000);
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0).WithMessage("Giá trị không được âm.");
        RuleFor(x => x.Value).LessThanOrEqualTo(100).WithMessage("Tỷ lệ phần trăm tối đa 100.")
            .When(x => x.RuleType is PriceRuleType.OverheadPercent or PriceRuleType.ProfitMarginPercent);
        RuleFor(x => x.Value).InclusiveBetween(0.05m, 5m).WithMessage("Hệ số vật liệu nên từ 0,05 đến 5.")
            .When(x => x.RuleType == PriceRuleType.MaterialUsageFactor);
        RuleFor(x => x.Value).LessThanOrEqualTo(1_000_000_000m).WithMessage("Giá trị quá lớn.");
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).WithMessage("Ngày kết thúc phải sau ngày bắt đầu.")
            .When(x => x.EffectiveFrom.HasValue && x.EffectiveTo.HasValue);
    }
}

/// <summary>/admin/price-rules - the parameters of the custom-furniture price calculator.</summary>
public interface IPriceRuleAdminService
{
    Task<IReadOnlyList<PriceRuleDto>> ListAsync(PriceRuleType? type, CancellationToken cancellationToken = default);
    Task<PriceRuleCommand> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(int? id, PriceRuleCommand command, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class PriceRuleAdminService(
    IPriceRuleRepository rules,
    IRepository<ProductMaterial> materials,
    IValidator<PriceRuleCommand> validator,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLog) : IPriceRuleAdminService
{
    public Task<IReadOnlyList<PriceRuleDto>> ListAsync(PriceRuleType? type, CancellationToken cancellationToken = default) =>
        rules.ListAsync(type, cancellationToken);

    public async Task<PriceRuleCommand> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var r = await rules.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("tham số giá", id);
        return new PriceRuleCommand
        {
            Code = r.Code, Name = r.Name, RuleType = r.RuleType, FurnitureType = r.FurnitureType, MaterialId = r.MaterialId, FinishType = r.FinishType,
            Value = r.Value, Unit = r.Unit, Priority = r.Priority, IsActive = r.IsActive, Description = r.Description,
            EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo
        };
    }

    public async Task<int> SaveAsync(int? id, PriceRuleCommand command, CancellationToken cancellationToken = default)
    {
        command.Code = (command.Code ?? string.Empty).Trim().ToUpperInvariant();
        // Dates picked without a time: the end date is inclusive (valid until the end of that day).
        if (command.EffectiveTo is DateTime to && to.TimeOfDay == TimeSpan.Zero)
        {
            command.EffectiveTo = to.Date.AddDays(1).AddTicks(-1);
        }

        await validator.EnsureValidAsync(command, cancellationToken);
        if (await rules.CodeExistsAsync(command.Code, id, cancellationToken))
        {
            throw new AppValidationException(nameof(PriceRuleCommand.Code), "Mã này đã được dùng.");
        }

        if (command.MaterialId is int materialId && !await materials.AnyAsync(m => m.Id == materialId, cancellationToken))
        {
            throw new AppValidationException(nameof(PriceRuleCommand.MaterialId), "Chất liệu không tồn tại.");
        }

        PriceRule rule;
        object? before = null;
        if (id is int existingId)
        {
            rule = await rules.GetByIdAsync(existingId, cancellationToken) ?? throw new NotFoundException("tham số giá", existingId);
            before = new { rule.Value, rule.IsActive, rule.Priority, rule.EffectiveFrom, rule.EffectiveTo };
        }
        else
        {
            rule = new PriceRule();
            await rules.AddAsync(rule, cancellationToken);
        }

        rule.Code = command.Code;
        rule.Name = command.Name.Trim();
        rule.RuleType = command.RuleType;
        rule.FurnitureType = command.FurnitureType;
        rule.MaterialId = command.MaterialId;
        rule.FinishType = command.FinishType;
        rule.Value = command.Value;
        rule.Unit = command.Unit.Trim();
        rule.Priority = command.Priority;
        rule.IsActive = command.IsActive;
        rule.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        rule.EffectiveFrom = command.EffectiveFrom;
        rule.EffectiveTo = command.EffectiveTo;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(id is null ? AuditAction.Create : AuditAction.Update, nameof(PriceRule), rule.Id.ToString(),
            $"{(id is null ? "Thêm" : "Sửa")} tham số giá {rule.Code}", OldValues: before,
            NewValues: new { rule.Value, rule.IsActive, rule.Priority, rule.EffectiveFrom, rule.EffectiveTo }), cancellationToken);
        return rule.Id;
    }

    public async Task SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var rule = await rules.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("tham số giá", id);
        rule.IsActive = active;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(PriceRule), id.ToString(), $"{(active ? "Bật" : "Tắt")} tham số giá {rule.Code}"), cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await rules.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("tham số giá", id);
        rules.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(PriceRule), id.ToString(), $"Xóa tham số giá {rule.Code}",
            OldValues: new { rule.Code, rule.Value }), cancellationToken);
    }
}
