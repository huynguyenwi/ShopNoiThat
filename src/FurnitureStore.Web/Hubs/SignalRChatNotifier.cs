using FurnitureStore.Application.Chat;
using FurnitureStore.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace FurnitureStore.Web.Hubs;

/// <summary>Delivers chat events through <see cref="ChatHub"/> to the customer's connections and to the staff group.</summary>
public sealed class SignalRChatNotifier(IHubContext<ChatHub, IChatClient> hub) : IChatNotifier
{
    public async Task MessageSentAsync(ChatCustomer customer, ChatConversationDto conversation, ChatMessageDto message, CancellationToken cancellationToken = default)
    {
        await hub.Clients.Group(ChatHub.StaffGroup).ReceiveMessage(message, conversation);
        await hub.Clients.Group(ChatHub.CustomerGroup(customer)).ReceiveMessage(message, ForCustomer(conversation));
    }

    public Task MessagesReadAsync(ChatCustomer customer, ChatConversationDto conversation, ChatSenderType reader, CancellationToken cancellationToken = default) =>
        reader == ChatSenderType.Staff
            ? hub.Clients.Groups(ChatHub.StaffGroup, ChatHub.CustomerGroup(customer)).MessagesRead(conversation.Id, reader)
            : hub.Clients.Group(ChatHub.StaffGroup).MessagesRead(conversation.Id, reader);

    /// <summary>Customers do not need internal fields such as the assigned staff id.</summary>
    private static ChatConversationDto ForCustomer(ChatConversationDto conversation) =>
        conversation with { AssignedStaffId = null, StaffUnreadCount = 0 };
}
