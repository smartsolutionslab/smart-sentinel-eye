using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 300 (#2349) T011 — the bad-request table from spec.md
/// §"Acceptance scenarios / Bad request", at the API level. Phase-6
/// backend-review findings B1 (a <c>Box</c>/<c>Ellipse</c> carrying
/// <c>text</c>/<c>fontSizePx</c> was silently accepted) and B3 (these
/// examples had no API-level test, and <c>color</c>/<c>kind</c> parse
/// failures surfaced the guard's own parameter name <c>value</c> instead of
/// the wire field) are what this file closes.
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayElementValidationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ElementBody(
        string kind = "Text",
        string? color = "#FFFFFFD9",
        string? text = "Zone A",
        int? fontSizePx = 24,
        decimal normalizedX = 0.1m,
        decimal normalizedY = 0.1m,
        decimal normalizedWidth = 0.2m,
        decimal normalizedHeight = 0.2m) => new
        {
            kind,
            color,
            text,
            fontSizePx,
            normalizedX,
            normalizedY,
            normalizedWidth,
            normalizedHeight,
        };

    private async Task<JsonElement> CreateExpectingBadRequestAsync(object element, string expectedField)
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage response = await overlays.PostAsJsonAsync(
            "/overlays",
            new { name = $"Val-{Guid.NewGuid():N}"[..16], elements = new[] { element } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_INVALID_INPUT");
        string detail = problem.GetProperty("detail").GetString()!;
        // Quoted: ArgumentException appends "(Parameter 'x')" using the
        // thrown exception's own paramName, which is the actual FR-003
        // signal. An unquoted check on "kind" would pass by coincidence —
        // "ElementKind" always contains "Kind" — whether or not the
        // endpoint actually names the wire field.
        detail.ShouldContain($"'{expectedField}'");
        return problem;
    }

    [Fact]
    public async Task An_unknown_kind_is_rejected_naming_kind() =>
        await CreateExpectingBadRequestAsync(ElementBody(kind: "Arrow"), "kind");

    [Fact]
    public async Task A_named_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: "red"), "color");

    [Fact]
    public async Task A_four_digit_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: "#FF00"), "color");

    [Fact]
    public async Task A_seven_digit_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: "#FF0000A"), "color");

    [Fact]
    public async Task A_nine_digit_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: "#FF0000AAB"), "color");

    [Fact]
    public async Task A_non_hex_digit_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: "#GG0000"), "color");

    [Fact]
    public async Task A_missing_colour_is_rejected_naming_color() =>
        await CreateExpectingBadRequestAsync(ElementBody(color: null), "color");

    /// <summary>B1 — a <c>Box</c> carrying <c>text</c> must 400 naming <c>text</c>, not silently drop it.</summary>
    [Fact]
    public async Task A_box_carrying_text_is_rejected_naming_text() =>
        await CreateExpectingBadRequestAsync(
            ElementBody(kind: "Box", text: "x", fontSizePx: null), "text");

    [Fact]
    public async Task A_text_element_with_no_text_is_rejected_naming_text() =>
        await CreateExpectingBadRequestAsync(
            ElementBody(kind: "Text", text: null, fontSizePx: 24), "text");

    /// <summary>B1 — a <c>Box</c> carrying <c>fontSizePx</c> must 400 naming <c>fontSizePx</c>, not silently drop it.</summary>
    [Fact]
    public async Task A_box_carrying_a_font_size_is_rejected_naming_font_size_px() =>
        await CreateExpectingBadRequestAsync(
            ElementBody(kind: "Box", text: null, fontSizePx: 24), "fontSizePx");

    [Fact]
    public async Task A_box_of_zero_width_is_rejected_naming_normalized_width() =>
        await CreateExpectingBadRequestAsync(
            ElementBody(kind: "Box", text: null, fontSizePx: null, normalizedWidth: 0m), "normalizedWidth");

    /// <summary>Spec 300 "Shapes count toward the ceiling" — any mix of kinds counts toward <c>MaxElements = 8</c>.</summary>
    [Fact]
    public async Task Five_boxes_and_four_texts_are_rejected_naming_the_ceiling_of_8_elements()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        object[] elements =
        [
            .. Enumerable.Range(0, 5).Select(_ => ElementBody(kind: "Box", text: null, fontSizePx: null)),
            .. Enumerable.Range(0, 4).Select(_ => ElementBody(kind: "Text")),
        ];

        HttpResponseMessage response = await overlays.PostAsJsonAsync(
            "/overlays",
            new { name = $"Ceil-{Guid.NewGuid():N}"[..16], elements });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_ELEMENTS_TOO_MANY");
        problem.GetProperty("detail").GetString()!.ShouldContain("8");
    }
}
