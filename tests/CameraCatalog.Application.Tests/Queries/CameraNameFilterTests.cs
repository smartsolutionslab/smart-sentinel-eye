using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Finding a camera by name (spec 055).
///
/// <para>
/// <b>The dangerous output here is a filtered list with a confident, wrong
/// total.</b> Every consumer uses the reported count to decide whether it holds
/// everything, so a filter returning matches beside the catalogue's total tells
/// an operator there are five when two matched — and tells a caller it is
/// missing three that do not exist. That is the first test below, and it is the
/// one this feature can fail quietly.
/// </para>
/// </summary>
public class CameraNameFilterTests
{
    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    /// <summary>
    /// **The gate.** Kills the mutation that counts before filtering — the only
    /// one in this feature whose survival produces a plausible answer rather
    /// than an obviously broken one.
    /// </summary>
    [Fact]
    public async Task A_filtered_total_counts_the_matches_and_not_the_catalogue()
    {
        ListCamerasQueryHandler handler = NewHandler(
            CameraNamed("Line 2 Furnace"),
            CameraNamed("Furnace 3"),
            CameraNamed("Bay 4 Inlet"),
            CameraNamed("Coiler"),
            CameraNamed("Cooling Bed"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "furn" },
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        result.Value.Count.ShouldBe(
            2,
            "the total must describe the matches; five would tell a caller it is missing three that do not exist");
    }

    /// <summary>
    /// **The whole feature.** The browser's own type-ahead already finds
    /// "Furnace 3" by its start; what it cannot do is find "Line 2 Furnace", and
    /// fab naming routinely puts the distinguishing word last.
    /// </summary>
    [Fact]
    public async Task A_fragment_matches_in_the_middle_of_a_name()
    {
        ListCamerasQueryHandler handler = NewHandler(
            CameraNamed("Line 2 Furnace"),
            CameraNamed("Bay 4 Inlet"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "furn" },
            CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Line 2 Furnace");
    }

    /// <summary>
    /// Case and surrounding whitespace are both normalised away before matching.
    ///
    /// <para>
    /// One test rather than two because the assertion is one: <b>what the
    /// operator typed is normalised, then matched</b>. Split, the two bodies
    /// were identical and said so only by their names.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("FURN")]
    [InlineData("furn")]
    [InlineData("FuRn")]
    [InlineData("  furn  ")]
    [InlineData("\tfurn\n")]
    public async Task A_fragment_is_trimmed_and_case_folded_before_matching(string fragment)
    {
        ListCamerasQueryHandler handler = NewHandler(CameraNamed("Line 2 Furnace"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = fragment },
            CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Line 2 Furnace");
    }

    /// <summary>
    /// **A cleared search box must return the catalogue, not empty it.**
    /// Treating a blank fragment as "match nothing" is the difference between a
    /// filter an operator can clear and one that strands them.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_absent_or_blank_fragment_returns_everything(string? fragment)
    {
        ListCamerasQueryHandler handler = NewHandler(
            CameraNamed("Line 2 Furnace"),
            CameraNamed("Bay 4 Inlet"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = fragment },
            CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        result.Value.Count.ShouldBe(2);
    }

    /// <summary>
    /// **Accents do not fold, and that is a decision.** Matching reuses the
    /// normalisation the uniqueness constraint uses, so "matches" and "is the
    /// same name" agree. A search folding what uniqueness keeps would show two
    /// distinct cameras as one match.
    /// </summary>
    [Fact]
    public async Task An_accented_name_is_not_matched_by_its_unaccented_fragment()
    {
        ListCamerasQueryHandler handler = NewHandler(CameraNamed("Fuernace"), CameraNamed("Fürnace"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "für" },
            CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Fürnace");
    }

    /// <summary>
    /// **The fragment is text, not a pattern.** An operator types words, and a
    /// per-cent sign is a character in a name. Getting this wrong is both a
    /// wrong answer and a trust-boundary failure, since the fragment arrives
    /// over HTTP.
    /// </summary>
    [Fact]
    public async Task A_wildcard_character_matches_itself_and_not_everything()
    {
        ListCamerasQueryHandler handler = NewHandler(
            CameraNamed("50% Load"),
            CameraNamed("Bay 4 Inlet"));

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "%" },
            CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("50% Load");
        result.Value.Count.ShouldBe(1);
    }

    /// <summary>
    /// Filtering and paging compose: every page is drawn from the matches, and
    /// the total stays the match count rather than reverting to the catalogue on
    /// a later page.
    /// </summary>
    [Fact]
    public async Task Filtering_and_paging_compose()
    {
        ListCamerasQueryHandler handler = NewHandler(
            CameraNamed("Furnace A"),
            CameraNamed("Furnace B"),
            CameraNamed("Furnace C"),
            CameraNamed("Bay 4 Inlet"));

        Result<CameraListPageDto, ListCamerasError> first = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "furnace", Sort = "name", Order = "asc", Limit = 2 },
            CancellationToken.None);

        Result<CameraListPageDto, ListCamerasError> second = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "furnace", Sort = "name", Order = "asc", Limit = 2, Offset = 2 },
            CancellationToken.None);

        first.Value.Items.Select(item => item.Name).ShouldBe(["Furnace A", "Furnace B"]);
        second.Value.Items.Select(item => item.Name).ShouldBe(["Furnace C"]);

        first.Value.Count.ShouldBe(3);
        second.Value.Count.ShouldBe(
            3,
            "the total is the match count on every page, not the catalogue on the last");
    }

    /// <summary>
    /// The filter narrows; it does not widen. A retired camera whose name
    /// matches stays out unless retired cameras were asked for.
    /// </summary>
    [Fact]
    public async Task Filtering_does_not_reach_past_the_retired_exclusion()
    {
        Camera staying = CameraNamed("Line 2 Furnace");
        Camera going = CameraNamed("Old Furnace");
        going.Retire(AnAdmin, new FixedClock(DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture)));

        ListCamerasQueryHandler handler = NewHandler(staying, going);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { NameFragment = "furnace" },
            CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Line 2 Furnace");
        result.Value.Count.ShouldBe(1);
    }

    private static ListCamerasQuery DefaultQuery() =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: ListCamerasDefaults.DefaultSort,
            Order: ListCamerasDefaults.DefaultOrder,
            Offset: ListCamerasDefaults.DefaultOffset,
            Limit: ListCamerasDefaults.DefaultLimit,
            IncludeRetired: false);

    private static ListCamerasQueryHandler NewHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));

    private static Camera CameraNamed(string name) =>
        Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.5.10/h264"),
            AnAdmin,
            new FixedClock(DateTimeOffset.Parse("2026-05-20T10:00:00Z", CultureInfo.InvariantCulture)));
}
