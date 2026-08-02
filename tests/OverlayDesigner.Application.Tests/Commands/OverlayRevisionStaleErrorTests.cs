using System.Net;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

/// <summary>
/// ADR-0047 gives every command its own error union, so the stale-version
/// case is declared five times here as well. ADR-0104 accepts that
/// duplication — both within this context and against LayoutComposition —
/// but asks for it to be kept in sync by hand. This is the guard that
/// catches it drifting.
/// </summary>
public class OverlayRevisionStaleErrorTests
{
    private const string ExpectedCode = "OVERLAY_REVISION_STALE";

    private static readonly Guid Overlay = Guid.CreateVersion7();

    private static ApiError[] EveryStaleCase() =>
    [
        new PublishRevisionError.OverlayRevisionStale(Overlay, 3, 4),
        new ArchiveRevisionError.OverlayRevisionStale(Overlay, 3, 4),
        new BranchDraftRevisionError.OverlayRevisionStale(Overlay, 3, 4),
        new EditDraftRevisionError.OverlayRevisionStale(Overlay, 3, 4),
        new RevertRevisionError.OverlayRevisionStale(Overlay, 3, 4),
    ];

    [Fact]
    public void Every_mutating_command_has_a_stale_case()
    {
        EveryStaleCase().Length.ShouldBe(5);
    }

    [Fact]
    public void Every_stale_case_reports_the_same_code()
    {
        EveryStaleCase().ShouldAllBe(error => error.Code == ExpectedCode);
    }

    [Fact]
    public void Every_stale_case_maps_to_409_conflict()
    {
        EveryStaleCase().ShouldAllBe(error => error.Status == HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_message_names_both_the_version_held_and_the_version_stored()
    {
        foreach (ApiError error in EveryStaleCase())
        {
            error.Message.ShouldContain("3");
            error.Message.ShouldContain("4");
        }
    }

    // The two revisioned contexts must stay parallel (ADR-0104), but their
    // error codes are deliberately context-local: a client keying off the code
    // should not have to guess which aggregate it came from.
    [Fact]
    public void The_code_is_namespaced_to_this_context()
    {
        ExpectedCode.ShouldNotBe("LAYOUT_REVISION_STALE");
        EveryStaleCase().ShouldAllBe(error => error.Code.StartsWith("OVERLAY_", StringComparison.Ordinal));
    }
}
