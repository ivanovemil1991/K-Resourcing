namespace Claims.Application.Abstractions;

/// <summary>Handles a command: a request that changes state.</summary>
/// <typeparam name="TCommand">The command to handle.</typeparam>
/// <typeparam name="TResult">What the command returns.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Runs the command.</summary>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles a query: a request that only reads and never changes state.</summary>
/// <typeparam name="TQuery">The query to handle.</typeparam>
/// <typeparam name="TResult">What the query returns.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Runs the query.</summary>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
