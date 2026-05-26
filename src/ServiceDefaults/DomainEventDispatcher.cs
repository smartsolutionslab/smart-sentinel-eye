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
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            DomainEventInvoker invoker = InvokersByEventType.GetOrAdd(
                domainEvent.GetType(), BuildInvoker);
            await invoker.InvokeAsync(services, domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DomainEventInvoker BuildInvoker(Type eventType)
    {
        Type handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        Type enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        System.Reflection.MethodInfo handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))
            ?? throw new InvalidOperationException(
                $"Expected Handle method on {handlerType.FullName}.");

        return new DomainEventInvoker(enumerableType, handleMethod);
    }

    private sealed class DomainEventInvoker(Type enumerableHandlerType, System.Reflection.MethodInfo handleMethod)
    {
        public async Task InvokeAsync(
            IServiceProvider provider, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            object handlers = provider.GetRequiredService(enumerableHandlerType);
            foreach (object handler in (System.Collections.IEnumerable)handlers)
            {
                Task task = (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
                await task.ConfigureAwait(false);
            }
        }
    }
}
