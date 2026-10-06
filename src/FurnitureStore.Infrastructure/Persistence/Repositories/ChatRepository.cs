using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

public sealed class ChatRepository(ApplicationDbContext context) : EfRepository<ChatConversation>(context), IChatRepository
{
    public Task<ChatConversation?> FindLatestForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default)
    {
        var query = Context.ChatConversations.AsQueryable();
        query = customer.UserId is not null
            ? query.Where(c => c.CustomerId == customer.UserId)
            : query.Where(c => c.CustomerId == null && c.GuestKey == customer.GuestKey);

        return query.OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<ChatConversationDto?> GetDtoAsync(int conversationId, CancellationToken cancellationToken = default) =>
        Project(Context.ChatConversations.AsNoTracking().Where(c => c.Id == conversationId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeMessageId, int take, CancellationToken cancellationToken = default)
    {
        var query = Context.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (beforeMessageId.HasValue)
        {
            query = query.Where(m => m.Id < beforeMessageId.Value);
        }

        var newestFirst = await query
            .OrderByDescending(m => m.Id)
            .Take(take)
            .Select(m => new ChatMessageDto(m.Id, m.ConversationId, m.SenderType, m.SenderName, m.Content, m.SentAt, m.IsRead))
            .ToListAsync(cancellationToken);
        newestFirst.Reverse();
        return newestFirst;
    }

    public async Task<PagedResult<ChatConversationDto>> SearchAsync(AdminChatQuery query, CancellationToken cancellationToken = default)
    {
        var conversations = Context.ChatConversations.AsNoTracking();
        if (query.Status.HasValue) conversations = conversations.Where(c => c.Status == query.Status);
        if (query.UnreadOnly) conversations = conversations.Where(c => c.StaffUnreadCount > 0);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search;
            conversations = conversations.Where(c =>
                c.CustomerName.Contains(term)
                || (c.CustomerEmail != null && c.CustomerEmail.Contains(term))
                || (c.CustomerPhone != null && c.CustomerPhone.Contains(term))
                || c.Messages.Any(m => m.Content.Contains(term)));
        }

        var total = await conversations.CountAsync(cancellationToken);
        var rows = await Project(conversations
                .OrderByDescending(c => c.StaffUnreadCount > 0)
                .ThenByDescending(c => c.LastMessageAt)
                .ThenByDescending(c => c.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<ChatConversationDto>(rows, total, query.Page, query.PageSize);
    }

    public Task<int> CountRecentMessagesAsync(int conversationId, ChatSenderType senderType, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Context.ChatMessages.CountAsync(m => m.ConversationId == conversationId && m.SenderType == senderType && m.SentAt >= sinceUtc, cancellationToken);

    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
        await Context.ChatMessages.AddAsync(message, cancellationToken);

    public Task<int> MarkReadAsync(int conversationId, ChatSenderType writer, DateTime readAtUtc, CancellationToken cancellationToken = default) =>
        Context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.SenderType == writer && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true).SetProperty(m => m.ReadAt, readAtUtc), cancellationToken);

    public Task<int> CountConversationsWaitingForStaffAsync(CancellationToken cancellationToken = default) =>
        Context.ChatConversations.CountAsync(c => c.StaffUnreadCount > 0, cancellationToken);

    private static IQueryable<ChatConversationDto> Project(IQueryable<ChatConversation> query) =>
        query.Select(c => new ChatConversationDto(
            c.Id, c.CustomerName, c.CustomerEmail, c.CustomerPhone, c.CustomerId, c.CustomerId == null,
            c.ProductId, c.Product != null ? c.Product.Name : null, c.Product != null ? c.Product.Slug : null,
            c.Status, c.CreatedAt, c.LastMessageAt, c.LastMessagePreview, c.CustomerUnreadCount, c.StaffUnreadCount, c.AssignedStaffId));
}
