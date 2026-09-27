using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode.Events;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.SourceMode;

/// <summary>
/// Phase 4a (spec 269 T003a). Exercises the aggregate against the signatures
/// T001 introduced with their behaviour withheld — every assertion here is
/// expected to fail until T004 fills the bodies in, except the three
/// null-guard cases (T001 wrote those guards already; see each test's
/// remark).
/// </summary>
public class SourceModeTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Declare_sets_the_declared_mode()
    {
        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .WithMode(Domain.SourceMode.EventTypeMode.Strict)
            .Build();

        sourceMode.Mode.ShouldBe(Domain.SourceMode.EventTypeMode.Strict);
    }

    [Fact]
    public void Declare_records_who_declared_it_and_when()
    {
        OperatorIdentifier declaredBy = OperatorIdentifier.From(Guid.CreateVersion7());

        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .DeclaredBy(declaredBy)
            .At(Now)
            .Build();

        sourceMode.Declaration.ShouldBe(
            Domain.SourceMode.Declaration.From(Domain.SourceMode.DeclaredAt.From(Now), declaredBy));
    }

    [Fact]
    public void Declare_raises_a_SourceModeDeclaredDomainEvent_naming_fab_source_and_mode()
    {
        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .WithFab("dresden")
            .WithSource(Source.Manual)
            .WithMode(Domain.SourceMode.EventTypeMode.Strict)
            .Build();

        SourceModeDeclaredDomainEvent raised = sourceMode.PendingEvents
            .OfType<SourceModeDeclaredDomainEvent>()
            .ShouldHaveSingleItem();

        raised.Fab.ShouldBe(FabIdentifier.From("dresden"));
        raised.Source.ShouldBe(Source.Manual);
        raised.Mode.ShouldBe(Domain.SourceMode.EventTypeMode.Strict);
    }

    [Fact]
    public void Change_to_a_different_mode_flips_it_and_raises_a_SourceModeChangedDomainEvent()
    {
        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .WithMode(Domain.SourceMode.EventTypeMode.Strict)
            .Build();
        sourceMode.ClearPendingEvents();

        sourceMode.Change(
            Domain.SourceMode.EventTypeMode.Discovery,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now.AddHours(1)));

        sourceMode.Mode.ShouldBe(Domain.SourceMode.EventTypeMode.Discovery);
        SourceModeChangedDomainEvent raised = sourceMode.PendingEvents
            .OfType<SourceModeChangedDomainEvent>()
            .ShouldHaveSingleItem();
        raised.From.ShouldBe(Domain.SourceMode.EventTypeMode.Strict);
        raised.To.ShouldBe(Domain.SourceMode.EventTypeMode.Discovery);
    }

    [Fact]
    public void Change_to_the_same_mode_raises_nothing_and_leaves_the_declaration()
    {
        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .WithMode(Domain.SourceMode.EventTypeMode.Strict)
            .At(Now)
            .Build();
        Domain.SourceMode.Declaration originalDeclaration = sourceMode.Declaration;
        sourceMode.ClearPendingEvents();

        sourceMode.Change(
            Domain.SourceMode.EventTypeMode.Strict,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now.AddHours(1)));

        sourceMode.Mode.ShouldBe(Domain.SourceMode.EventTypeMode.Strict);
        sourceMode.Declaration.ShouldBe(originalDeclaration, "a no-op change must not replace the declaration");
        sourceMode.PendingEvents.ShouldBeEmpty("a repeat of the same mode must not raise a second event");
    }

    [Fact]
    public void Change_records_who_changed_it_and_when()
    {
        Domain.SourceMode.SourceMode sourceMode = new SourceModeBuilder()
            .WithMode(Domain.SourceMode.EventTypeMode.Discovery)
            .Build();
        OperatorIdentifier changedBy = OperatorIdentifier.From(Guid.CreateVersion7());
        DateTimeOffset changedAt = Now.AddHours(2);

        sourceMode.Change(Domain.SourceMode.EventTypeMode.Strict, changedBy, new FakeClock(changedAt));

        sourceMode.Declaration.ShouldBe(
            Domain.SourceMode.Declaration.From(Domain.SourceMode.DeclaredAt.From(changedAt), changedBy));
    }

    /// <summary>
    /// T001 wrote these guards fully — they are the plumbing, not the
    /// behaviour under test — so all three are expected to pass on the first
    /// run (spec 269 tasks.md T003a.7).
    /// </summary>
    [Fact]
    public void Declare_refuses_a_null_fab()
    {
        Should.Throw<ArgumentNullException>(() => Domain.SourceMode.SourceMode.Declare(
            null!,
            Source.Manual,
            Domain.SourceMode.EventTypeMode.Strict,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now)));
    }

    /// <summary>Expected to pass on the first run — see <see cref="Declare_refuses_a_null_fab"/>.</summary>
    [Fact]
    public void Declare_refuses_a_null_source()
    {
        Should.Throw<ArgumentNullException>(() => Domain.SourceMode.SourceMode.Declare(
            FabIdentifier.From("dresden"),
            null!,
            Domain.SourceMode.EventTypeMode.Strict,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now)));
    }

    /// <summary>Expected to pass on the first run — see <see cref="Declare_refuses_a_null_fab"/>.</summary>
    [Fact]
    public void Declare_refuses_a_null_mode()
    {
        Should.Throw<ArgumentNullException>(() => Domain.SourceMode.SourceMode.Declare(
            FabIdentifier.From("dresden"),
            Source.Manual,
            null!,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now)));
    }
}
