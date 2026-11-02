using Marketplace.Domain.Common;

namespace Marketplace.Application.Abstractions;

// D-05: hand-written handlers resolved from DI, with no mediator library. Expected outcomes return Result (D-07).
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

// Returned by commands that have no value to report.
public readonly record struct Unit
{
    public static readonly Unit Value;
}
