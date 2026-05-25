namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Marker for command records. Each command carries the type of result its
/// handler returns. Per ADR-0042 + ADR-0057, Wolverine dispatches the handler
/// behind ICommandHandler&lt;,&gt; — application code does not import Wolverine.
/// </summary>
public interface ICommand<TResult>
    where TResult : notnull;

/// <summary>
/// Marker for query records.
/// </summary>
public interface IQuery<TResult>
    where TResult : notnull;
