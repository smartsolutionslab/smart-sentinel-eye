using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class AggregateRootTests
{
    private readonly record struct SampleId(Guid Value) : IStronglyTypedId<Guid>;
    private sealed record SampleEvent(string Tag) : IDomainEvent;

    private sealed class SampleAggregate : AggregateRoot<SampleId>
    {
        public SampleAggregate()
        {
            Id = new SampleId(Guid.CreateVersion7());
        }

        public void DoSomething(string tag) => Raise(new SampleEvent(tag));

        public void BumpVersion() => Version++;
    }

    [Fact]
    public void New_aggregate_has_no_pending_events()
    {
        SampleAggregate aggregate = new();

        aggregate.PendingEvents.ShouldBeEmpty();
        aggregate.Version.ShouldBe(0);
    }

    [Fact]
    public void Raise_appends_events_in_order()
    {
        SampleAggregate aggregate = new();

        aggregate.DoSomething("a");
        aggregate.DoSomething("b");

        aggregate.PendingEvents.Count.ShouldBe(2);
        aggregate.PendingEvents[0].ShouldBeOfType<SampleEvent>().Tag.ShouldBe("a");
        aggregate.PendingEvents[1].ShouldBeOfType<SampleEvent>().Tag.ShouldBe("b");
    }

    [Fact]
    public void ClearPendingEvents_empties_the_buffer()
    {
        SampleAggregate aggregate = new();
        aggregate.DoSomething("a");

        aggregate.ClearPendingEvents();

        aggregate.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Version_is_mutable_via_protected_setter()
    {
        SampleAggregate aggregate = new();

        aggregate.BumpVersion();

        aggregate.Version.ShouldBe(1);
    }
}
