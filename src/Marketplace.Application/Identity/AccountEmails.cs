using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Identity;

// Where links in account emails point: the web client's base URL (configuration key Email:LinkBaseUrl).
public sealed class AccountLinkOptions
{
    public string LinkBaseUrl { get; set; } = "http://localhost:3000";
}

// Issues a single-use token. Older unused tokens of the same purpose for that user are marked used (section 9.1).
public sealed class OneTimeTokenService(IAppDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    public async Task<string> IssueAsync(Guid userId, OneTimeTokenPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.OneTimeTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.UsedAt, now), cancellationToken);

        var (raw, hash) = SecureTokens.Generate();
        db.OneTimeTokens.Add(OneTimeToken.Issue(userId, purpose, hash, now, lifetime));
        await db.SaveChangesAsync(cancellationToken);
        return raw;
    }
}

internal static class UserMessages
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static Guid ReadUserId(string payload) =>
        JsonSerializer.Deserialize<UserMessage>(payload, _json)?.UserId ?? throw new JsonException("The payload has no userId.");

    public static string Link(AccountLinkOptions options, string path, string token) =>
        $"{options.LinkBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";
}

// Section 9.10. Every handler re-checks that the user still exists, and skips work that is already done.
public sealed class EmailVerificationRequestedHandler(
    IIdentityService identity, OneTimeTokenService tokens, IEmailSender email, IOptions<AccountLinkOptions> links) : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.EmailVerificationRequested;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var account = await identity.GetAccountAsync(UserMessages.ReadUserId(payload), cancellationToken);
        if (account is null || account.EmailConfirmed)
        {
            return;
        }

        var token = await tokens.IssueAsync(account.UserId, OneTimeTokenPurpose.EmailVerification, OneTimeTokenService.VerificationLifetime, cancellationToken);
        await email.SendAsync(
            new EmailMessage(
                account.Email,
                "Confirm your email address",
                $"Hi {account.DisplayName},\n\nConfirm your email address within 24 hours:\n{UserMessages.Link(links.Value, "verify-email", token)}\n\nIf you did not create an account, ignore this email."),
            cancellationToken);
    }
}

public sealed class AccountExistsNoticeHandler(IIdentityService identity, IEmailSender email) : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.AccountExistsNotice;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var account = await identity.GetAccountAsync(UserMessages.ReadUserId(payload), cancellationToken);
        if (account is null)
        {
            return;
        }

        await email.SendAsync(
            new EmailMessage(
                account.Email,
                "Someone tried to register with your email",
                $"Hi {account.DisplayName},\n\nSomeone tried to create an account with this email address, which already has an account. If it was you, sign in or reset your password. Otherwise, you can ignore this email."),
            cancellationToken);
    }
}

public sealed class PasswordResetRequestedHandler(
    IIdentityService identity, OneTimeTokenService tokens, IEmailSender email, IOptions<AccountLinkOptions> links) : IOutboxHandler
{
    public string MessageType => OutboxMessageTypes.PasswordResetRequested;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var account = await identity.GetAccountAsync(UserMessages.ReadUserId(payload), cancellationToken);
        if (account is null)
        {
            return;
        }

        var token = await tokens.IssueAsync(account.UserId, OneTimeTokenPurpose.PasswordReset, OneTimeTokenService.ResetLifetime, cancellationToken);
        await email.SendAsync(
            new EmailMessage(
                account.Email,
                "Reset your password",
                $"Hi {account.DisplayName},\n\nReset your password within 30 minutes:\n{UserMessages.Link(links.Value, "reset-password", token)}\n\nIf you did not ask for this, ignore this email."),
            cancellationToken);
    }
}
