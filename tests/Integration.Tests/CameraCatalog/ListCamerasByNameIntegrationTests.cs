using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Finding a camera by name, through the real contract (spec 055).
///
/// <para>
/// <b>These exist because the match rule has two implementations and only one
/// of them is the truth.</b> The handler tests run against an in-memory fake
/// that reaches the normalised name through <c>CameraName.NormalizedValue</c>;
/// the real source reaches a generated column through <c>EF.Property</c>. The
/// two agree today and are written in different languages. Only a test that
/// asks Postgres can tell when they stop agreeing — a handler test would prove
/// the fake agrees with itself.
/// </para>
///
/// <para>
/// The wildcard case matters most here for the same reason: whether a per-cent
/// sign is a character or a pattern is decided by the database, not by the fake.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ListCamerasByNameIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The case the browser's own type-ahead cannot serve: the distinguishing
    /// word is not at the start.
    /// </summary>
    [Fact]
    public async Task A_fragment_finds_a_camera_whose_name_does_not_start_with_it()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Line 2 Furnace", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Furnace 3", "rtsp://10.0.5.2/h264");
        await RegisterAsync(client, "Bay 4 Inlet", "rtsp://10.0.5.3/h264");

        JsonElement page = await ListAsync(client, "?name=furn");

        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("count").GetInt32().ShouldBe(
            2,
            "the total must count the matches; three would say two cameras are missing that are not");
    }

    [Fact]
    public async Task The_match_ignores_case_through_the_real_column()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Line 2 Furnace", "rtsp://10.0.5.1/h264");

        JsonElement upper = await ListAsync(client, "?name=FURN");
        JsonElement lower = await ListAsync(client, "?name=furn");

        upper.GetProperty("count").GetInt32().ShouldBe(1);
        lower.GetProperty("count").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// **Two normalisers, and this is the only thing that checks they agree.**
    ///
    /// <para>
    /// The fragment is upper-cased in the handler by .NET
    /// (<c>ToUpperInvariant</c>); the column it is compared against is
    /// upper-cased by Postgres (<c>upper(name)</c>, a stored generated column).
    /// Every handler test runs against the in-memory fake, so all of them
    /// exercise the .NET side twice and none of them exercise Postgres at all —
    /// including the one asserting that accents do not fold.
    /// </para>
    ///
    /// <para>
    /// They do agree, and ADR-0137 records the behaviour they agree on. But
    /// they agree <i>because this database is</i> <c>en_US.utf8</c>: under a
    /// <c>C</c> ctype Postgres leaves <c>ü</c> alone while .NET does not, and an
    /// accented camera becomes unfindable by the very fragment that names it.
    /// That is a property of how the database was created rather than of this
    /// code, which is why it is asserted rather than assumed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_accented_name_normalises_the_same_way_on_both_sides()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Fürnace", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Fuernace", "rtsp://10.0.5.2/h264");

        JsonElement accented = await ListAsync(client, "?name=f%C3%BCr");
        JsonElement plain = await ListAsync(client, "?name=fur");

        // The accented fragment finds the accented name: the two upper-casings
        // agree, case-insensitively, on a character outside ASCII.
        accented.GetProperty("count").GetInt32().ShouldBe(1);
        accented.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Fürnace");

        // And the unaccented fragment does not — accents are not folded, which
        // is the half of the rule an operator has to be told.
        plain.GetProperty("count").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// **The one the fake cannot answer.** Whether the fragment is text or a
    /// pattern is decided by how the query reaches the database, so a per-cent
    /// sign matching everything would pass every handler test and fail here.
    /// </summary>
    [Fact]
    public async Task A_wildcard_character_is_matched_literally()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "50% Load", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Bay 4 Inlet", "rtsp://10.0.5.2/h264");
        await RegisterAsync(client, "Coiler", "rtsp://10.0.5.3/h264");

        JsonElement page = await ListAsync(client, "?name=%25");

        page.GetProperty("count").GetInt32().ShouldBe(
            1,
            "a per-cent sign is a character in a name, not a wildcard; three would mean the fragment "
            + "reached the database as a pattern");
        page.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("50% Load");
    }

    /// <summary>
    /// An underscore is the other pattern character, and it fails the same way —
    /// silently, by matching one of anything.
    /// </summary>
    [Fact]
    public async Task An_underscore_is_matched_literally()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Bay_4", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Bay 4", "rtsp://10.0.5.2/h264");

        JsonElement page = await ListAsync(client, "?name=Bay_4");

        page.GetProperty("count").GetInt32().ShouldBe(
            1,
            "an underscore matches itself; two would mean it reached the database as single-character "
            + "wildcard and matched the space as well");
        page.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Bay_4");
    }

    /// <summary>
    /// A cleared search box returns the catalogue rather than emptying it, and
    /// the query string is where that is most easily got wrong — an empty
    /// parameter is present, not absent.
    /// </summary>
    [Fact]
    public async Task An_empty_fragment_in_the_query_string_returns_everything()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Line 2 Furnace", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Bay 4 Inlet", "rtsp://10.0.5.2/h264");

        JsonElement page = await ListAsync(client, "?name=");

        page.GetProperty("count").GetInt32().ShouldBe(2);
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    /// <summary>
    /// A fragment nothing contains is an empty page, not a failure. "No camera
    /// is called that" is an answer an operator needs to be able to tell from a
    /// request they got wrong.
    /// </summary>
    [Fact]
    public async Task A_fragment_nothing_matches_is_an_empty_page_rather_than_an_error()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Line 2 Furnace", "rtsp://10.0.5.1/h264");

        HttpResponseMessage response = await client.GetAsync("/cameras?name=nothingiscalledthis");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("count").GetInt32().ShouldBe(0);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// Filtering and paging compose against the real query: the second page is
    /// drawn from the matches, and the total stays their number.
    /// </summary>
    [Fact]
    public async Task Filtering_and_paging_compose_through_the_endpoint()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Furnace A", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Furnace B", "rtsp://10.0.5.2/h264");
        await RegisterAsync(client, "Furnace C", "rtsp://10.0.5.3/h264");
        await RegisterAsync(client, "Bay 4 Inlet", "rtsp://10.0.5.4/h264");

        JsonElement second = await ListAsync(client, "?name=furnace&sort=name&order=asc&offset=2&limit=2");

        second.GetProperty("count").GetInt32().ShouldBe(3, "the match count, not the catalogue, on a later page");
        second.GetProperty("items").GetArrayLength().ShouldBe(1);
        second.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Furnace C");
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/cameras{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task RegisterAsync(HttpClient client, string name, string rtspUrl)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras", new { name, rtspUrl });
        response.EnsureSuccessStatusCode();
    }
}
