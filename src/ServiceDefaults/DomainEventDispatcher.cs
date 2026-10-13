using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Resolves <see cref="IDomainEventHandler{TEvent}"/> via DI for each event's
/// runtime type and invokes them sequentially. The reflected method lookup
/// is cached per event type so per-event dispatch is O(1) after first use.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventInvoker> InvokersByEventType = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvents).IsNotNull();

        foreach (var domainEvent in domainEvents)
        {
            var invoker = InvokersByEventType.GetOrAdd(domainEvent.GetType(), BuildInvoker);
            await invoker.InvokeAsync(services, domainEvent, cancellationToken);
        }
    }

    private static DomainEventInvoker BuildInvoker(Type eventType)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle)) ?? throw new InvalidOperationException($"Expected Handle method on {handlerType.FullName}.");

        return new DomainEventInvoker(enumerableType, handleMethod);
    }

    private sealed class DomainEventInvoker(Type enumerableHandlerType, System.Reflection.MethodInfo handleMethod)
    {
        public async Task InvokeAsync(IServiceProvider provider, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            var handlers = provider.GetRequiredService(enumerableHandlerType);
            foreach (var handler in (System.Collections.IEnumerable)handlers)
            {
                var task = (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
                await task;
            }
        }
    }
}
