namespace FurnitureStore.Application.Common.Interfaces;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? ToName = null);

/// <summary>
/// Sends transactional emails (welcome, password reset, order confirmation...).
/// Development uses a pickup directory (emails saved as files) so reset links never end up in logs.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
