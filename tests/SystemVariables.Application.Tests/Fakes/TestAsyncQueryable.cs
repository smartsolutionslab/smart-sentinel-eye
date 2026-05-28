using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IVariableQuerySource"/> for query-handler tests:
/// wraps a <c>List&lt;Variable&gt;</c> in an async-enumerable queryable
/// so EF Core's async extensions (<c>SingleOrDefaultAsync</c>,
/// <c>ToListAsync</c>) work without a real DbContext.
/// </summary>
internal sealed class TestVariableQuerySource(IEnumerable<Variable> seed) : IVariableQuerySource
{
    public IQueryable<Variable> Variables { get; } =
        new TestAsyncEnumerable<Variable>(seed);
}

internal sealed class TestAsyncEnumerable<T>(IEnumerable<T> enumerable)
    : EnumerableQuery<T>(enumerable), IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(Expression expression) : this(((IQueryable<T>)new EnumerableQuery<T>(expression)).ToList()) { }

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
        // EF's async extensions wrap the sync result in Task<T> via this overload.
        object? executionResult = ((IQueryProvider)this).Execute(expression);
        Type resultType = typeof(TResult);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Type innerType = resultType.GetGenericArguments()[0];
            var taskFromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(innerType);
            return (TResult)taskFromResult.Invoke(null, [executionResult])!;
        }
        return (TResult)executionResult!;
    }
}
