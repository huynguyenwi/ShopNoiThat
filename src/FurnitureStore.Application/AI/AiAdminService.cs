using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.AI;

public sealed record AiKnowledgeDto(int Id, string Title, string Content, string Category, string? Keywords, bool IsActive, int DisplayOrder, DateTime? UpdatedAt);

public sealed class AiKnowledgeCommand
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "Chính sách";

    /// <summary>Comma separated words that trigger this entry, e.g. "giao hàng, vận chuyển, ship".</summary>
    public string? Keywords { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed class AiKnowledgeCommandValidator : AbstractValidator<AiKnowledgeCommand>
{
    public AiKnowledgeCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Vui lòng nhập tiêu đề.").MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().WithMessage("Vui lòng nhập nội dung.").MaximumLength(2000).WithMessage("Nội dung tối đa 2.000 ký tự.");
        RuleFor(x => x.Category).NotEmpty().WithMessage("Vui lòng nhập nhóm.").MaximumLength(100);
        RuleFor(x => x.Keywords).MaximumLength(500);
        RuleFor(x => x.DisplayOrder).InclusiveBetween(0, 10_000);
    }
}

/// <summary>Back-office: AI conversations (read only) and the chatbot's knowledge base.</summary>
public interface IAiAdminService
{
    bool AiEnabled { get; }
    Task<PagedResult<AiConversationListItemDto>> ListConversationsAsync(AdminAiConversationQuery query, CancellationToken cancellationToken = default);
    Task<AiConversationDto> GetConversationAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiKnowledgeDto>> ListKnowledgeAsync(CancellationToken cancellationToken = default);
    Task<AiKnowledgeDto> GetKnowledgeAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveKnowledgeAsync(int? id, AiKnowledgeCommand command, CancellationToken cancellationToken = default);
    Task DeleteKnowledgeAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class AiAdminService(
    IAiConversationRepository conversations,
    IAiKnowledgeRepository knowledge,
    IProductFactRepository facts,
    IAiChatClient aiClient,
    IValidator<AiKnowledgeCommand> validator,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLog) : IAiAdminService
{
    public bool AiEnabled => aiClient.IsConfigured;

    public Task<PagedResult<AiConversationListItemDto>> ListConversationsAsync(AdminAiConversationQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        return conversations.SearchAsync(query, cancellationToken);
    }

    public Task<AiConversationDto> GetConversationAsync(int id, CancellationToken cancellationToken = default) =>
        AiConversationReader.ReadAsync(conversations, facts, id, cancellationToken);

    public async Task<IReadOnlyList<AiKnowledgeDto>> ListKnowledgeAsync(CancellationToken cancellationToken = default) =>
        (await knowledge.ListAsync(cancellationToken: cancellationToken))
            .OrderBy(k => k.Category).ThenBy(k => k.DisplayOrder).ThenBy(k => k.Id)
            .Select(Map).ToList();

    public async Task<AiKnowledgeDto> GetKnowledgeAsync(int id, CancellationToken cancellationToken = default) =>
        Map(await knowledge.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("nội dung chatbot", id));

    public async Task<int> SaveKnowledgeAsync(int? id, AiKnowledgeCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        AIKnowledgeEntry entry;
        if (id is int existingId)
        {
            entry = await knowledge.GetByIdAsync(existingId, cancellationToken) ?? throw new NotFoundException("nội dung chatbot", existingId);
        }
        else
        {
            entry = new AIKnowledgeEntry();
            await knowledge.AddAsync(entry, cancellationToken);
        }

        entry.Title = command.Title.Trim();
        entry.Content = command.Content.Trim();
        entry.Category = command.Category.Trim();
        entry.Keywords = string.IsNullOrWhiteSpace(command.Keywords)
            ? null
            : string.Join(", ", command.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
        entry.IsActive = command.IsActive;
        entry.DisplayOrder = command.DisplayOrder;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(new AuditEntry(id is null ? AuditAction.Create : AuditAction.Update, nameof(AIKnowledgeEntry), entry.Id.ToString(),
            $"{(id is null ? "Thêm" : "Sửa")} nội dung chatbot \"{entry.Title}\""), cancellationToken);
        return entry.Id;
    }

    public async Task DeleteKnowledgeAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await knowledge.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("nội dung chatbot", id);
        knowledge.Remove(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(AIKnowledgeEntry), id.ToString(), $"Xóa nội dung chatbot \"{entry.Title}\""), cancellationToken);
    }

    private static AiKnowledgeDto Map(AIKnowledgeEntry k) => new(k.Id, k.Title, k.Content, k.Category, k.Keywords, k.IsActive, k.DisplayOrder, k.UpdatedAt ?? k.CreatedAt);
}
