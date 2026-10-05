using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 300 (#2349) T009 — raw-SQL coverage of the <c>CHECK</c> constraints
/// added by <c>20261005105722_AddOverlayElementKindAndColour</c>, bypassing
/// the application layer entirely so the backstop is tested as a backstop.
/// Mirrors <c>OverlayRevisionLifecycleIntegrationTests.InsertRawLabelAsync</c>.
///
/// <para>
/// Phase-6 finding S1: <c>ck_overlay_revision_elements_text_matches_kind</c>
/// originally read <c>(kind = 'Text') = (text IS NOT NULL AND font_size_px
/// IS NOT NULL)</c>, which is satisfied by a <c>Box</c> carrying exactly one
/// of <c>text</c>/<c>font_size_px</c> (<c>false = false</c>). The two
/// half-filled cases below are what exposed that and must stay red against
/// the un-split constraint.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayElementCheckConstraintIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_box_carrying_both_text_and_font_size_violates_the_presence_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Box", color: "#D32F2FFF", text: "'x'", fontSizePx: "16"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    /// <summary>S1 — a Box with text but no font size must still be rejected.</summary>
    [Fact]
    public async Task A_box_carrying_text_with_no_font_size_violates_the_presence_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Box", color: "#D32F2FFF", text: "'x'", fontSizePx: "NULL"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    /// <summary>S1 — a Box with a font size but no text must still be rejected.</summary>
    [Fact]
    public async Task A_box_carrying_a_font_size_with_no_text_violates_the_presence_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Box", color: "#D32F2FFF", text: "NULL", fontSizePx: "24"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_lower_case_colour_violates_the_format_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Text", color: "#d32f2fff", text: "'x'", fontSizePx: "16"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_six_digit_colour_violates_the_format_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Text", color: "#D32F2F", text: "'x'", fontSizePx: "16"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_text_element_at_alpha_74_violates_the_alpha_floor_check()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        PostgresException ex = await Should.ThrowAsync<PostgresException>(() => InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Text", color: "#FFFFFF74", text: "'x'", fontSizePx: "16"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    /// <summary>Strokes carry no readability floor (ADR-0165 §2) — alpha 00 on a Box is accepted.</summary>
    [Fact]
    public async Task A_box_at_alpha_00_is_accepted()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        await InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Box", color: "#FFFFFF00", text: "NULL", fontSizePx: "NULL");
    }

    /// <summary>
    /// A row shaped exactly as the migration's backfill leaves every
    /// pre-spec-300 row — <c>Text</c>/<c>#FFFFFFD9</c> with both components
    /// present — is accepted and reads back unchanged through the API.
    /// </summary>
    [Fact]
    public async Task A_pre_migration_shaped_row_reads_back_as_Text_at_the_default_colour()
    {
        Guid overlayIdentifier = await CreateDraftAsync();

        await InsertRawElementAsync(
            overlayIdentifier, ordinal: 1, kind: "Text", color: "#FFFFFFD9", text: "'Legacy'", fontSizePx: "16");

        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");
        elements.GetArrayLength().ShouldBe(2);
        elements[1].GetProperty("kind").GetString().ShouldBe("Text");
        elements[1].GetProperty("color").GetString().ShouldBe("#FFFFFFD9");
        elements[1].GetProperty("text").GetString().ShouldBe("Legacy");
    }

    private async Task<Guid> CreateDraftAsync()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Chk-{Guid.NewGuid():N}"[..16],
                elements = new[]
                {
                    new
                    {
                        kind = "Text",
                        color = "#FFFFFFD9",
                        text = "Production Line 1",
                        normalizedX = 0.5m,
                        normalizedY = 0.05m,
                        normalizedWidth = 0.3m,
                        normalizedHeight = 0.08m,
                        fontSizePx = 48,
                    },
                },
            });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>
    /// Inserts one <c>overlay_revision_elements</c> row directly via SQL for
    /// the overlay's revision 1, bypassing the domain and EF entirely —
    /// mirrors <c>OverlayRevisionLifecycleIntegrationTests.InsertRawLabelAsync</c>.
    /// <paramref name="text"/> and <paramref name="fontSizePx"/> are raw SQL
    /// literals (a quoted string, a number, or the bare word <c>NULL</c>)
    /// rather than parameters, because a parameterised <c>NULL</c> needs an
    /// explicit cast Npgsql cannot infer from <c>object?</c> alone.
    /// </summary>
    private async Task InsertRawElementAsync(
        Guid overlayIdentifier, int ordinal, string kind, string color, string text, string fontSizePx)
    {
        await using OverlayDesignerDbContext db = await aspire.CreateOverlayDesignerDbContextAsync();

        string sql =
            "INSERT INTO overlay_revision_elements " +
            "(revision_id, ordinal, kind, color, text, x, y, width, height, font_size_px) " +
            $"SELECT revision_id, {{0}}, '{kind}', '{color}', {text}, 0.1, 0.1, 0.2, 0.2, {fontSizePx} " +
            "FROM overlay_revisions WHERE overlay_id = {1} AND revision_number = 1;";

        await db.Database.ExecuteSqlRawAsync(sql, ordinal, overlayIdentifier);
    }
}
