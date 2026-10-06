using System.Text;
using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Application.Chat;

public interface IChatService
{
    // ---- customer side
    Task<CustomerChatDto> GetForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default);
    Task<ChatConversationDto> StartAsync(ChatCustomer customer, StartChatCommand command, CancellationToken cancellationToken = default);
    Task<ChatMessageDto> SendFromCustomerAsync(ChatCustomer customer, SendChatMessageCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessageDto>> GetOlderForCustomerAsync(ChatCustomer customer, int beforeMessageId, CancellationToken cancellationToken = default);
    Task MarkReadByCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default);
    Task<ChatConversationDto?> FindForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>After sign-in: the conversation started as a guest now belongs to the account (if it has none yet).</summary>
    Task<bool> ClaimGuestConversationAsync(string guestKey, string userId, string displayName, CancellationToken cancellationToken = default);

    // ---- staff side
    Task<PagedResult<ChatConversationDto>> ListForStaffAsync(AdminChatQuery query, CancellationToken cancellationToken = default);
    Task<StaffThreadDto> OpenForStaffAsync(int conversationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessageDto>> GetOlderForStaffAsync(int conversationId, int beforeMessageId, CancellationToken cancellationToken = default);
    Task<ChatMessageDto> SendFromStaffAsync(ChatStaff staff, int conversationId, string? content, CancellationToken cancellationToken = default);
    Task MarkReadByStaffAsync(int conversationId, CancellationToken cancellationToken = default);
    Task<ChatConversationDto> SetStatusAsync(ChatStaff staff, int conversationId, ConversationStatus status, CancellationToken cancellationToken = default);
    Task<int> CountWaitingAsync(CancellationToken cancellationToken = default);

    /// <summary>Server-side routing only (e.g. typing indicators): the customer identity behind a conversation.</summary>
    Task<ChatCustomer> GetCustomerOfAsync(int conversationId, CancellationToken cancellationToken = default);
}

public sealed class StartChatCommandValidator : AbstractValidator<StartChatCommand>
{
    public StartChatCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên của bạn.").MaximumLength(150);
        RuleFor(x => x.Phone).Matches(ValidationPatterns.VietnamesePhone).WithMessage(ValidationPatterns.VietnamesePhoneMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Email).EmailAddress().WithMessage("Email không hợp lệ.").MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .OverridePropertyName(nameof(StartChatCommand.Phone))
            .WithMessage("Vui lòng nhập số điện thoại hoặc email để cửa hàng liên hệ lại.");
    }
}

public sealed class ChatService(
    IChatRepository conversations,
    IRepository<Product> products,
    IValidator<StartChatCommand> startValidator,
    INotificationService notifications,
    IChatNotifier notifier,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ChatService> logger) : IChatService
{
    public const int PageSize = 30;
    public const int CustomerMessagesPerMinute = 15;
    public const int StaffMessagesPerMinute = 60;
    private const int PreviewLength = 120;

    // ================================================================== customer

    public async Task<CustomerChatDto> GetForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default)
    {
        if (!customer.IsKnown)
        {
            return new CustomerChatDto(null, [], false, RequiresContactInfo: true);
        }

        var conversation = await conversations.FindLatestForCustomerAsync(customer, cancellationToken);
        if (conversation is null)
        {
            return new CustomerChatDto(null, [], false, RequiresContactInfo: customer.UserId is null);
        }

        if (conversation.CustomerUnreadCount > 0)
        {
            await MarkReadAsync(conversation, reader: ChatSenderType.Customer, cancellationToken);
        }

        var messages = await conversations.GetMessagesAsync(conversation.Id, null, PageSize + 1, cancellationToken);
        var dto = await RequireDtoAsync(conversation.Id, cancellationToken);
        return new CustomerChatDto(dto, TrimPage(messages), messages.Count > PageSize, RequiresContactInfo: false);
    }

    public async Task<ChatConversationDto> StartAsync(ChatCustomer customer, StartChatCommand command, CancellationToken cancellationToken = default)
    {
        if (!customer.IsKnown)
        {
            throw new BusinessRuleException("Không xác định được phiên chat. Vui lòng tải lại trang.");
        }

        if (customer.UserId is null)
        {
            await startValidator.EnsureValidAsync(command, cancellationToken);
        }

        var conversation = await conversations.FindLatestForCustomerAsync(customer, cancellationToken);
        if (conversation is null)
        {
            conversation = await CreateAsync(customer, command, cancellationToken);
        }
        else if (customer.UserId is null)
        {
            // A returning guest may correct their contact details.
            conversation.CustomerName = Clean(command.Name, 150) ?? conversation.CustomerName;
            conversation.CustomerPhone = Clean(command.Phone, 20);
            conversation.CustomerEmail = Clean(command.Email, 256);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await RequireDtoAsync(conversation.Id, cancellationToken);
    }

    public async Task<ChatMessageDto> SendFromCustomerAsync(ChatCustomer customer, SendChatMessageCommand command, CancellationToken cancellationToken = default)
    {
        var content = NormalizeContent(command.Content);
        if (!customer.IsKnown)
        {
            throw new BusinessRuleException("Vui lòng nhập thông tin liên hệ trước khi chat.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = await conversations.FindLatestForCustomerAsync(customer, cancellationToken);
        if (conversation is null)
        {
            if (customer.UserId is null)
            {
                throw new BusinessRuleException("Vui lòng nhập thông tin liên hệ trước khi chat.");
            }

            conversation = await CreateAsync(customer, new StartChatCommand { ProductId = command.ProductId }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (await conversations.CountRecentMessagesAsync(conversation.Id, ChatSenderType.Customer, now.AddMinutes(-1), cancellationToken) >= CustomerMessagesPerMinute)
        {
            throw new BusinessRuleException("Bạn gửi tin nhắn quá nhanh, vui lòng đợi một chút rồi thử lại.");
        }

        var wasWaiting = conversation.StaffUnreadCount > 0;
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            SenderId = customer.UserId,
            SenderType = ChatSenderType.Customer,
            SenderName = conversation.CustomerName,
            Content = content,
            SentAt = now
        };

        await conversations.AddMessageAsync(message, cancellationToken);
        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Open;
            conversation.ClosedAt = null;
        }

        conversation.LastMessageAt = now;
        conversation.LastMessagePreview = Preview(content);
        conversation.StaffUnreadCount++;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!wasWaiting)
        {
            // One notification per "wave" of unanswered messages, not one per message.
            await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.NewChatMessage, $"Tin nhắn mới từ {conversation.CustomerName}",
                conversation.LastMessagePreview ?? string.Empty, $"/admin/chat?id={conversation.Id}", cancellationToken);
        }

        var messageDto = ToDto(message);
        await PushAsync(n => n.MessageSentAsync(AudienceOf(conversation), RequireDto(conversation, message), messageDto, cancellationToken), conversation.Id, cancellationToken);
        logger.LogInformation("Chat message {MessageId} from customer in conversation {ConversationId}", message.Id, conversation.Id);
        return messageDto;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetOlderForCustomerAsync(ChatCustomer customer, int beforeMessageId, CancellationToken cancellationToken = default)
    {
        var conversation = customer.IsKnown ? await conversations.FindLatestForCustomerAsync(customer, cancellationToken) : null;
        return conversation is null ? [] : await conversations.GetMessagesAsync(conversation.Id, beforeMessageId, PageSize, cancellationToken);
    }

    public async Task MarkReadByCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default)
    {
        var conversation = customer.IsKnown ? await conversations.FindLatestForCustomerAsync(customer, cancellationToken) : null;
        if (conversation is { CustomerUnreadCount: > 0 })
        {
            await MarkReadAsync(conversation, ChatSenderType.Customer, cancellationToken);
        }
    }

    public async Task<ChatConversationDto?> FindForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default)
    {
        var conversation = customer.IsKnown ? await conversations.FindLatestForCustomerAsync(customer, cancellationToken) : null;
        return conversation is null ? null : await conversations.GetDtoAsync(conversation.Id, cancellationToken);
    }

    public async Task<bool> ClaimGuestConversationAsync(string guestKey, string userId, string displayName, CancellationToken cancellationToken = default)
    {
        var guestConversation = await conversations.FindLatestForCustomerAsync(new ChatCustomer(null, guestKey), cancellationToken);
        if (guestConversation is null || await conversations.FindLatestForCustomerAsync(new ChatCustomer(userId, null), cancellationToken) is not null)
        {
            return false;
        }

        guestConversation.CustomerId = userId;
        guestConversation.GuestKey = null;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            guestConversation.CustomerName = Clean(displayName, 150)!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Guest conversation {ConversationId} linked to user {UserId}", guestConversation.Id, userId);
        return true;
    }

    // ================================================================== staff

    public Task<PagedResult<ChatConversationDto>> ListForStaffAsync(AdminChatQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return conversations.SearchAsync(query, cancellationToken);
    }

    public async Task<StaffThreadDto> OpenForStaffAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        if (conversation.StaffUnreadCount > 0)
        {
            await MarkReadAsync(conversation, ChatSenderType.Staff, cancellationToken);
        }

        var messages = await conversations.GetMessagesAsync(conversationId, null, PageSize + 1, cancellationToken);
        return new StaffThreadDto(await RequireDtoAsync(conversationId, cancellationToken), TrimPage(messages), messages.Count > PageSize);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetOlderForStaffAsync(int conversationId, int beforeMessageId, CancellationToken cancellationToken = default)
    {
        _ = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        return await conversations.GetMessagesAsync(conversationId, beforeMessageId, PageSize, cancellationToken);
    }

    public async Task<ChatMessageDto> SendFromStaffAsync(ChatStaff staff, int conversationId, string? content, CancellationToken cancellationToken = default)
    {
        var text = NormalizeContent(content);
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (await conversations.CountRecentMessagesAsync(conversationId, ChatSenderType.Staff, now.AddMinutes(-1), cancellationToken) >= StaffMessagesPerMinute)
        {
            throw new BusinessRuleException("Gửi quá nhiều tin nhắn trong một phút, vui lòng thử lại sau.");
        }

        var customerHadUnread = conversation.CustomerUnreadCount > 0;
        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderId = staff.UserId,
            SenderType = ChatSenderType.Staff,
            SenderName = Clean(staff.DisplayName, 150) ?? "Nhân viên",
            Content = text,
            SentAt = now
        };

        await conversations.AddMessageAsync(message, cancellationToken);
        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Open;
            conversation.ClosedAt = null;
        }

        conversation.AssignedStaffId ??= staff.UserId;
        conversation.LastMessageAt = now;
        conversation.LastMessagePreview = Preview(text);
        conversation.CustomerUnreadCount++;
        conversation.StaffUnreadCount = 0; // replying means the customer's messages have been read
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await conversations.MarkReadAsync(conversationId, ChatSenderType.Customer, now, cancellationToken);

        if (conversation.CustomerId is not null && !customerHadUnread)
        {
            await notifications.NotifyUserAsync(conversation.CustomerId, NotificationType.NewChatMessage, "Cửa hàng đã trả lời tin nhắn của bạn",
                Preview(text), "/?chat=open", cancellationToken);
        }

        var messageDto = ToDto(message);
        await PushAsync(n => n.MessageSentAsync(AudienceOf(conversation), RequireDto(conversation, message), messageDto, cancellationToken), conversationId, cancellationToken);
        logger.LogInformation("Chat message {MessageId} from staff {StaffId} in conversation {ConversationId}", message.Id, staff.UserId, conversationId);
        return messageDto;
    }

    public async Task MarkReadByStaffAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        if (conversation.StaffUnreadCount > 0)
        {
            await MarkReadAsync(conversation, ChatSenderType.Staff, cancellationToken);
        }
    }

    public async Task<ChatConversationDto> SetStatusAsync(ChatStaff staff, int conversationId, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            throw new AppValidationException("Trạng thái không hợp lệ.");
        }

        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        if (conversation.Status == status)
        {
            return await RequireDtoAsync(conversationId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        conversation.Status = status;
        conversation.ClosedAt = status == ConversationStatus.Closed ? now : null;
        var note = new ChatMessage
        {
            ConversationId = conversationId,
            SenderType = ChatSenderType.System,
            SenderName = "Hệ thống",
            Content = status == ConversationStatus.Closed
                ? $"Cuộc trò chuyện đã được {staff.DisplayName} kết thúc. Bạn có thể nhắn tiếp bất cứ lúc nào."
                : $"Cuộc trò chuyện đã được {staff.DisplayName} mở lại.",
            SentAt = now,
            IsRead = true,
            ReadAt = now
        };
        await conversations.AddMessageAsync(note, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = RequireDto(conversation, note);
        await PushAsync(n => n.MessageSentAsync(AudienceOf(conversation), dto, ToDto(note), cancellationToken), conversationId, cancellationToken);
        return await RequireDtoAsync(conversationId, cancellationToken);
    }

    public Task<int> CountWaitingAsync(CancellationToken cancellationToken = default) =>
        conversations.CountConversationsWaitingForStaffAsync(cancellationToken);

    public async Task<ChatCustomer> GetCustomerOfAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        return AudienceOf(conversation);
    }

    // ================================================================== helpers

    private async Task<ChatConversation> CreateAsync(ChatCustomer customer, StartChatCommand command, CancellationToken cancellationToken)
    {
        int? productId = null;
        if (command.ProductId is int id && await products.AnyAsync(p => p.Id == id && p.Status == ProductStatus.Active, cancellationToken))
        {
            productId = id;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = new ChatConversation
        {
            CustomerId = customer.UserId,
            GuestKey = customer.UserId is null ? customer.GuestKey : null,
            CustomerName = Clean(customer.UserId is null ? command.Name : customer.DisplayName, 150) ?? "Khách hàng",
            CustomerEmail = Clean(customer.UserId is null ? command.Email : customer.Email, 256),
            CustomerPhone = Clean(customer.UserId is null ? command.Phone : customer.Phone, 20),
            ProductId = productId,
            Status = ConversationStatus.Open,
            LastMessageAt = now
        };

        await conversations.AddAsync(conversation, cancellationToken);
        return conversation;
    }

    /// <summary>Marks the other side's messages as read and resets the reader's unread counter.</summary>
    private async Task MarkReadAsync(ChatConversation conversation, ChatSenderType reader, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var writer = reader == ChatSenderType.Staff ? ChatSenderType.Customer : ChatSenderType.Staff;
        if (reader == ChatSenderType.Staff)
        {
            conversation.StaffUnreadCount = 0;
        }
        else
        {
            conversation.CustomerUnreadCount = 0;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await conversations.MarkReadAsync(conversation.Id, writer, now, cancellationToken);

        var dto = await conversations.GetDtoAsync(conversation.Id, cancellationToken);
        if (dto is not null)
        {
            await PushAsync(n => n.MessagesReadAsync(AudienceOf(conversation), dto, reader, cancellationToken), conversation.Id, cancellationToken);
        }
    }

    private async Task PushAsync(Func<IChatNotifier, Task> push, int conversationId, CancellationToken cancellationToken)
    {
        try
        {
            await push(notifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The message is already saved; clients will see it on their next refresh.
            logger.LogWarning(ex, "Could not push realtime chat event for conversation {ConversationId}", conversationId);
        }
    }

    private static ChatCustomer AudienceOf(ChatConversation c) => new(c.CustomerId, c.CustomerId is null ? c.GuestKey : null);

    private async Task<ChatConversationDto> RequireDtoAsync(int conversationId, CancellationToken cancellationToken) =>
        await conversations.GetDtoAsync(conversationId, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);

    /// <summary>DTO built from the tracked entity (avoids a query per message; product details are not needed for pushes).</summary>
    private static ChatConversationDto RequireDto(ChatConversation c, ChatMessage last) => new(
        c.Id, c.CustomerName, c.CustomerEmail, c.CustomerPhone, c.CustomerId, c.CustomerId is null, c.ProductId, c.Product?.Name, c.Product?.Slug,
        c.Status, c.CreatedAt, c.LastMessageAt, c.LastMessagePreview ?? Preview(last.Content), c.CustomerUnreadCount, c.StaffUnreadCount, c.AssignedStaffId);

    private static IReadOnlyList<ChatMessageDto> TrimPage(IReadOnlyList<ChatMessageDto> messages) =>
        messages.Count > PageSize ? messages.Skip(messages.Count - PageSize).ToList() : messages;

    public static ChatMessageDto ToDto(ChatMessage m) => new(m.Id, m.ConversationId, m.SenderType, m.SenderName, m.Content, m.SentAt, m.IsRead);

    /// <summary>Trims, unifies line breaks and removes control characters. Content is stored and rendered as plain text.</summary>
    public static string NormalizeContent(string? content)
    {
        var text = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var sb = new StringBuilder(text.Length);
        var newlines = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                if (++newlines <= 2) sb.Append(ch);
                continue;
            }

            newlines = 0;
            if (!char.IsControl(ch) || ch == '\t')
            {
                sb.Append(ch);
            }
        }

        var result = sb.ToString().Trim();
        if (result.Length == 0)
        {
            throw new AppValidationException("Content", "Vui lòng nhập nội dung tin nhắn.");
        }

        if (result.Length > ChatMessage.MaxContentLength)
        {
            throw new AppValidationException("Content", $"Tin nhắn tối đa {ChatMessage.MaxContentLength} ký tự.");
        }

        return result;
    }

    private static string Preview(string content)
    {
        var singleLine = content.Replace('\n', ' ');
        return singleLine.Length <= PreviewLength ? singleLine : singleLine[..(PreviewLength - 1)] + "…";
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
