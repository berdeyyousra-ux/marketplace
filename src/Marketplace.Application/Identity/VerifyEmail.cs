using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Identity;

// EP-02 and US-02.
public sealed record VerifyEmailCommand(string Token);

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator() => RuleFor(command => command.Token).NotEmpty().MaximumLength(100);
}

public static class TokenErrors
{
    public static readonly Error InvalidToken =
        new("invalid_token", "This link is invalid, has already been used or has expired.", ErrorType.Validation);
}

public sealed class VerifyEmailCommandHandler(IAppDbContext db, IIdentityService identity, TimeProvider timeProvider)
    : ICommandHandler<VerifyEmailCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hash = SecureTokens.Hash(command.Token);
        var now = timeProvider.GetUtcNow();
        var token = await db.OneTimeTokens.AsNoTracking()
            .Where(candidate => candidate.TokenHash == hash && candidate.Purpose == OneTimeTokenPurpose.EmailVerification)
            .Select(candidate => new { candidate.Id, candidate.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (token is null)
        {
            return TokenErrors.InvalidToken;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        // Single use, even under concurrent requests: only one update can match the unused, unexpired row.
        var claimed = await db.OneTimeTokens
            .Where(candidate => candidate.Id == token.Id && candidate.UsedAt == null && candidate.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.UsedAt, now), cancellationToken);
        if (claimed != 1)
        {
            return TokenErrors.InvalidToken;
        }

        await identity.ConfirmEmailAsync(token.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
