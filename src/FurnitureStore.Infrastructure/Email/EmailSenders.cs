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
    private const string DefaultLocalFromAddress = "no-reply@furniture.local";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Smtp.Host))
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp:Host để gửi mail SMTP.");
        }

        var smtpUser = settings.Smtp.UserName?.Trim();
        var fromAddress = settings.FromAddress?.Trim();
        if ((string.IsNullOrWhiteSpace(fromAddress) || string.Equals(fromAddress, DefaultLocalFromAddress, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(smtpUser) && smtpUser.Contains('@'))
        {
            fromAddress = smtpUser;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress!, settings.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        mail.To.Add(new MailAddress(message.To, message.ToName, Encoding.UTF8));
        mail.ReplyToList.Add(new MailAddress(fromAddress!, settings.FromName, Encoding.UTF8));

        // HTML-only messages are a common spam signal: send a plain-text part alongside the HTML one.
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(ToPlainText(message.HtmlBody), Encoding.UTF8, "text/plain"));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(settings.Smtp.Host.Trim(), settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrEmpty(smtpUser))
        {
            var password = settings.Smtp.Password ?? string.Empty;
            if (settings.Smtp.Host.Contains("gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                password = password.Replace(" ", string.Empty);
            }

            client.Credentials = new NetworkCredential(smtpUser, password);
        }

        await client.SendMailAsync(mail, cancellationToken);
        logger.LogInformation("Email \"{Subject}\" sent to {Recipient} via SMTP host {Host}", message.Subject, message.To, settings.Smtp.Host);
    }

    /// <summary>Plain-text version of an HTML email; links become "text: url" so they stay usable.</summary>
    internal static string ToPlainText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<(head|style|script)[^>]*>.*?</\1>", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<a [^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", "$2: $1",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<(br|/p|/tr|/h\d)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\n\s*", "\n");
        return text.Trim();
    }
}
