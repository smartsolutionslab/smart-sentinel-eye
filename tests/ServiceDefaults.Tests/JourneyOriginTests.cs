using System.Diagnostics;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 026. A plant-floor event published from a background service has no
/// work in progress to inherit a cause from, so it begins as an orphan and
/// nothing it goes on to cause can be traced back to it. This supplies the
/// beginning; the messaging layer already carries it from there.
/// </summary>
public sealed class JourneyOriginTests : IDisposable
{
    private const string ApplicationName = "test-application";

    private readonly List<Activity> started = [];
    private readonly ActivityListener listener;

    public JourneyOriginTests()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = started.Add,
        };

        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose() => listener.Dispose();

    /// <summary>
    /// FR-001. The one thing the whole feature does: give the publish something
    /// to be caused by.
    /// </summary>
    [Fact]
    public void Beginning_a_journey_makes_it_the_work_in_progress()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("ingest plant-floor event"))
        {
            Activity journey = Activity.Current.ShouldNotBeNull();
            journey.OperationName.ShouldBe("ingest plant-floor event");
        }
    }

    /// <summary>
    /// FR-006 / SC-005, at the unit level — the batch guard's other half lives
    /// in the integration suite where a real batch exists.
    ///
    /// <para>
    /// Two hundred deliveries are stored together, and the cheap version of
    /// this feature begins one journey for the batch. That still produces a
    /// joined trace and still reads as correct from the effect end, while
    /// making "what did this event cause" unanswerable for every event in it.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_event_begins_its_own_journey()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("first"))
        {
            Activity.Current.ShouldNotBeNull();
        }

        using (origin.Begin("second"))
        {
            Activity.Current.ShouldNotBeNull();
        }

        started.Count.ShouldBe(2);
        started[1].TraceId.ShouldNotBe(started[0].TraceId, "two events must not share one journey");
    }

    /// <summary>
    /// A journey ends where the work does. Left running, the next event
    /// published on the same thread would be recorded as caused by the previous
    /// one — a fabricated relationship, which is worse than a missing one
    /// because it reads as an answer.
    /// </summary>
    [Fact]
    public void A_journey_ends_when_its_work_does()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("ingest plant-floor event"))
        {
            Activity.Current.ShouldNotBeNull();
        }

        Activity.Current.ShouldBeNull();
    }

    /// <summary>
    /// Nothing listening is the ordinary case in a unit test and in any service
    /// whose exporter is not wired. `StartActivity` answers null there, and a
    /// `using var` over a null reference is legal C# that silently does
    /// nothing — so the caller cannot tell "not sampled" from "not started".
    /// An inert handle keeps that out of every call site.
    /// </summary>
    [Fact]
    public void An_unlistened_journey_still_hands_back_a_handle()
    {
        using JourneyOrigin origin = new("nobody-is-listening-to-this");

        IJourney journey = origin.Begin("ingest plant-floor event");

        journey.ShouldNotBeNull();
        Should.NotThrow(() => journey.Failed(new InvalidOperationException("refused")));
        Should.NotThrow(journey.Dispose);
    }

    /// <summary>
    /// A journey that failed to begin and one that began and caused nothing look
    /// identical without this: same name, no children. The status is the only
    /// thing that tells a broken ingest from an event no rule matched.
    /// </summary>
    [Fact]
    public void A_failed_journey_is_recorded_as_failed()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (IJourney journey = origin.Begin("ingest plant-floor event"))
        {
            journey.Failed(new InvalidOperationException("the outbox refused the insert"));

            Activity recorded = Activity.Current.ShouldNotBeNull();
            recorded.Status.ShouldBe(ActivityStatusCode.Error);
            recorded.StatusDescription.ShouldBe("the outbox refused the insert");
        }
    }

    /// <summary>The ordinary case stays unmarked, or the status means nothing.</summary>
    [Fact]
    public void A_journey_that_was_not_failed_carries_no_error()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("ingest plant-floor event"))
        {
            Activity.Current.ShouldNotBeNull().Status.ShouldBe(ActivityStatusCode.Unset);
        }
    }

    /// <summary>
    /// The receiving service's span is a Consumer, so what parents it is a
    /// Producer. Asserted because the kind is the sort of detail that reads as
    /// cosmetic and is what makes the dashboard render this as a message being
    /// sent rather than as unexplained internal work.
    /// </summary>
    [Fact]
    public void A_journey_begins_as_the_sending_half_of_a_message()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("ingest plant-floor event"))
        {
            Activity journey = Activity.Current.ShouldNotBeNull();
            journey.Kind.ShouldBe(ActivityKind.Producer);
        }
    }

    /// <summary>
    /// The source is named for the application because
    /// <c>ConfigureOpenTelemetry</c> already exports that name. A source named
    /// anything else emits into silence that cannot be told from working —
    /// the failure `Extensions.cs` reads Wolverine's source name off the object
    /// to avoid.
    /// </summary>
    [Fact]
    public void The_source_is_named_for_the_application_that_exports_it()
    {
        using JourneyOrigin origin = new(ApplicationName);

        using (origin.Begin("ingest plant-floor event"))
        {
            Activity journey = Activity.Current.ShouldNotBeNull();
            journey.Source.Name.ShouldBe(ApplicationName);
        }
    }

    [Fact]
    public void An_application_must_be_named()
    {
        Should.Throw<ArgumentException>(() => new JourneyOrigin(" "));
    }
}
