using MailKit.Net.Smtp;
using MailKit.Security;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Marketplace.Infrastructure.Email;

// Section 16 "Email" configuration.
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = "localhost";

    public int SmtpPort { get; set; } = 1025;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }

    public string FromAddress { get; set; } = "noreply@marketplace.dev";

    public string FromName { get; set; } = "Marketplace";
}

// D-27: MailKit over SMTP (Mailpit locally). One 15-second timeout per operation and no inline retries;
// the outbox retries failed sends (section 10.2).
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        if (!string.IsNullOrEmpty(settings.SmtpUser))
        {
            await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
