using Moq;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 463 (#3108), ADR-0190: <c>POST /overlays</c> and the draft <c>PATCH</c> refuse an element colour
/// brighter than relative luminance 0.35 with a 400 naming <c>color</c>, the cap and the nearest allowed
/// colour, and store nothing. Elements are parsed before the version check, so the refusal order of
/// <c>EditDraftRefusalOrderCharacterisationTests</c> is unchanged.
/// </summary>
public class OverlayColourCapEndpointTests
{
    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly System.Text.Json.JsonSerializerOptions WebOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Box", "#A0A0A0")]
    [InlineData("Ellipse", "#FFFFFF")]
    [InlineData("Box", "#FFFFFF10")]
    [InlineData("Text", "#FFFFFFD9")]
    public async Task Create_refuses_a_bright_colour_naming_color_the_cap_and_the_nearest_allowed_colour(
        string kind,
        string color)
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Element(kind, color)])));

        response.Status.ShouldBe(400);
        response.Body.ShouldContain("(Parameter 'color')");
        response.Body.ShouldContain("0.35");
        response.Body.ShouldContain("#9F9F9F");
        host.Overlays.Verify(overlays => overlays.Add(It.IsAny<Overlay>()), Times.Never);
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Box", "#A0A0A0")]
    [InlineData("Ellipse", "#FFFFFF")]
    [InlineData("Box", "#FFFFFF10")]
    public async Task Edit_refuses_a_bright_colour_naming_color_the_cap_and_the_nearest_allowed_colour(
        string kind,
        string color)
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(host, existing.Id.Value, Json(new EditDraftRequest([Element(kind, color)])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldContain("(Parameter 'color')");
        response.Body.ShouldContain("0.35");
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_accepts_a_colour_at_the_cap()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Element("Box", "#9F9F9F")])));

        response.Status.ShouldBe(201);
    }

    [Fact]
    public async Task A_stale_edit_with_a_valid_colour_is_still_refused_as_stale()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(host, existing.Id.Value, Json(new EditDraftRequest([Element("Box", "#9F9F9F")])), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldContain("OVERLAY_REVISION_STALE");
    }

    [Fact]
    public async Task A_stale_edit_with_a_bright_colour_is_refused_on_the_body_before_the_version()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(host, existing.Id.Value, Json(new EditDraftRequest([Element("Box", "#FFFFFF")])), ifMatch: "5");

        response.Status.ShouldBe(400);
        response.Body.ShouldContain("(Parameter 'color')");
    }

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, WebOptions);

    private static ElementRequest Element(string kind, string color) =>
        kind == "Text"
            ? new(kind, color, 0.1m, 0.1m, 0.2m, 0.2m, "label", 14)
            : new(kind, color, 0.1m, 0.1m, 0.2m, 0.2m, null, null);

    private static Task<InProcessResponse> Post(OverlayDesignerEndpointHost host, string body) =>
        host.SendAsync(
            "POST",
            "/overlays/",
            OverlayDesignerEndpointHost.Operator(Subject),
            new InProcessRequest(BodyJson: body));

    private static Task<InProcessResponse> Patch(
        OverlayDesignerEndpointHost host,
        Guid identifier,
        string body,
        string ifMatch) =>
        host.SendAsync(
            "PATCH",
            "/overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}",
            OverlayDesignerEndpointHost.Operator(Subject),
            new InProcessRequest(
                Route: new Dictionary<string, string>
                {
                    ["overlayIdentifier"] = identifier.ToString(),
                    ["revisionNumber"] = "1",
                },
                BodyJson: body,
                Headers: new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" }));

    private static Overlay Existing(OverlayDesignerEndpointHost host)
    {
        var element = OverlayElement.Box(
            NormalizedPosition.From(0.1m, 0.1m),
            NormalizedSize.From(0.2m, 0.2m),
            OverlayColor.From("#FF0000"));
        var overlay = Overlay.CreateDraft(
            OverlayName.From("Main wall"), [element], OperatorIdentifier.From(Subject), host.Clock.Object);
        host.Overlays
            .Setup(overlays => overlays.GetByIdentifierAsync(overlay.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Overlay>.Some(overlay));

        return overlay;
    }
}
