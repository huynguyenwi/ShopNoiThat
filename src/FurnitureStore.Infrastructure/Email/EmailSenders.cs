using System.Net;
using System.Net.Mail;
using System.Text;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Email;

/// <summary>
/// Development sender: saves every email as an .html file in the pickup directory
/// (default App_Data/emails) instead of sending it. Open the file to follow links such as password reset.
/// </summary>
public sealed class PickupDirectoryEmailSender(
    IOptions<EmailSettings> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<PickupDirectoryEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var directory = Path.IsPathRooted(settings.PickupDirectory)
            ? settings.PickupDirectory
            : Path.Combine(environment.ContentRootPath, settings.PickupDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.html";
        var header = new StringBuilder()
            .AppendLine("<!--")
            .AppendLine($"From: {settings.FromName} <{settings.FromAddress}>")
            .AppendLine($"To: {message.ToName} <{message.To}>")
            .AppendLine($"Subject: {message.Subject}")
            .AppendLine("-->")
            .ToString();

        await File.WriteAllTextAsync(Path.Combine(directory, fileName), header + message.HtmlBody, Encoding.UTF8, cancellationToken);

        // The body (which may contain a reset link) is never logged.
        logger.LogInformation("Email \"{Subject}\" written to pickup directory as {FileName}", message.Subject, fileName);
    }
}

/// <summary>Sends email through SMTP. Credentials come from configuration secrets.</summary>
public sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.HtmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        mail.To.Add(new MailAddress(message.To, message.ToName, Encoding.UTF8));

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
        logger.LogInformation("Email \"{Subject}\" sent via SMTP host {Host}", message.Subject, settings.Smtp.Host);
    }
}
