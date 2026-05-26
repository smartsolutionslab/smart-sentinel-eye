using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using SmartSentinelEye.StreamDistribution.Application.Queries;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// In-memory IStreamQuerySource for query-handler tests. Wraps the
/// repository's internal list in a TestAsyncEnumerable so EF Core's
/// CountAsync / ToListAsync / SingleOrDefaultAsync extensions resolve
/// against an IAsyncQueryProvider (same pattern as the CameraCatalog
/// tests, ADR-0052).
/// </summary>
public sealed class InMemoryStreamQuerySource(InMemoryStreamRepository repository) : IStreamQuerySource
{
    public IQueryable<Domain.Stream.Stream> Streams =>
        new TestAsyncEnumerable<Domain.Stream.Stream>(repository.Streams);
}

internal sealed class TestAsyncEnumerable<T>(IEnumerable<T> source)
    : EnumerableQuery<T>(source), IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(Expression expression)
        : this(new EnumerableQuery<T>(expression).AsEnumerable())
    {
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;

    public ValueTask DisposeAsync()
    {
        inner.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
}

internal sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) =>
        new TestAsyncEnumerable<TEntity>(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new TestAsyncEnumerable<TElement>(expression);

    public object Execute(Expression expression) => inner.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        Type expectedResultType = typeof(TResult).GetGenericArguments()[0];
        object executionResult = typeof(IQueryProvider)
            .GetMethod(name: nameof(IQueryProvider.Execute), genericParameterCount: 1, types: [typeof(Expression)])!
            .MakeGenericMethod(expectedResultType)
            .Invoke(inner, [expression])!;

        return (TResult)typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(expectedResultType)
            .Invoke(null, [executionResult])!;
    }
}
