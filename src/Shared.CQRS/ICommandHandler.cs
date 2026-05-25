namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Handler contract for a command. Hand-rolled per ADR-0057; Wolverine
/// implements the dispatcher behind this interface.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : notnull
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handler contract for a query.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : notnull
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
