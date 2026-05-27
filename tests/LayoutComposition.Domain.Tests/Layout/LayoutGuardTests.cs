using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Targeted null-argument + state-guard coverage for the Layout
/// aggregate — the paths the higher-level state-machine tests don't
/// exercise (each aggregate behaviour validates its arguments at the
/// top before doing any work).
/// </summary>
public class LayoutGuardTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void CreateDraft_rejects_a_null_name()
    {
        Action act = () => Domain.Layout.Layout.CreateDraft(
            name: null!,
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void CreateDraft_rejects_a_null_clock()
    {
        Action act = () => Domain.Layout.Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void BranchDraft_rejects_a_null_clock()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.BranchDraft(
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void EditDraft_rejects_a_null_clock()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.EditDraft(
            LayoutRevisionNumber.One,
            CameraIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Publish_rejects_a_null_clock()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.Publish(
            LayoutRevisionNumber.One,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Revert_rejects_a_null_clock()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.Revert(
            LayoutRevisionNumber.One,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void ArchiveRevision_rejects_a_null_clock()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.ArchiveRevision(
            LayoutRevisionNumber.One,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Revert_on_a_Draft_revision_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        Action act = () => layout.Revert(
            LayoutRevisionNumber.One,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }
}
