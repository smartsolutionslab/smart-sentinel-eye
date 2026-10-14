using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IEventQuerySource"/> / <see cref="IDeadLetterQuerySource"/>
/// / <see cref="IWebhookIntegrationQuerySource"/> wrappers backed by
/// <see cref="TestAsyncEnumerable{T}"/> so EF Core's async extensions
/// (<c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>) work without a real
/// DbContext. Same pattern as
/// SystemVariables.Application.Tests/Fakes/TestAsyncQueryable.cs.
/// </summary>
internal sealed class TestEventQuerySource(IEnumerable<EventAggregate> seed) : IEventQuerySource
{
    public IQueryable<EventAggregate> Events { get; } =
        new TestAsyncEnumerable<EventAggregate>(seed);
}

internal sealed class TestDeadLetterQuerySource(IEnumerable<DeadLetter> seed) : IDeadLetterQuerySource
{
    public IQueryable<DeadLetter> DeadLetters { get; } = new TestAsyncEnumerable<DeadLetter>(seed);
}

internal sealed class TestWebhookIntegrationQuerySource(IEnumerable<WebhookIntegration> seed)
    : IWebhookIntegrationQuerySource
{
    public IQueryable<WebhookIntegration> WebhookIntegrations { get; } =
        new TestAsyncEnumerable<WebhookIntegration>(seed);
}

internal sealed class TestRegisteredEventTypeQuerySource(IEnumerable<RegisteredEventType> seed)
    : IRegisteredEventTypeQuerySource
{
    public IQueryable<RegisteredEventType> RegisteredEventTypes { get; } =
        new TestAsyncEnumerable<RegisteredEventType>(seed);
}

internal sealed class TestSourceModeQuerySource(IEnumerable<SourceMode> seed) : ISourceModeQuerySource
{
    public IQueryable<SourceMode> SourceModes { get; } = new TestAsyncEnumerable<SourceMode>(seed);
}

internal sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable) { }

    // Deferred, so an ordered result stays an IOrderedQueryable and a ThenBy
    // can follow an OrderBy; materialising here would break that chain.
    public TestAsyncEnumerable(Expression expression)
        : base(expression) { }

    IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;

    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }

    public ValueTask<bool> MoveNextAsync() => new(inner.MoveNext());
}

internal sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new TestAsyncEnumerable<TEntity>(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new TestAsyncEnumerable<TElement>(expression);

    public object? Execute(Expression expression) => inner.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var executionResult = ((IQueryProvider)this).Execute(expression);
        var resultType = typeof(TResult);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = resultType.GetGenericArguments()[0];
            var taskFromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(innerType);
            return (TResult)taskFromResult.Invoke(null, [executionResult])!;
        }
        return (TResult)executionResult!;
    }
}
