using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 029 US3, T018–T021 — another plant's cameras stay invisible, not merely
/// forbidden (FR-006, FR-007, SC-003).
///
/// <para>
/// This is the requirement spec 015 had to <b>withdraw</b>: without a
/// single-camera read there was nothing to refuse, so the non-enumeration
/// guarantee had nowhere to live. It comes last in this feature not because it
/// matters least but because it asserts that <em>both</em> endpoints refuse
/// identically, and the second one only arrived with US2.
/// </para>
///
/// <para>
/// Everything here compares the two refusals <b>field by field</b>. A test that
/// checked only the status code would pass against an implementation that
/// helpfully explained the camera belongs to Munich — and a camera record
/// carries its RTSP address, so anything distinguishable lets an operator
/// enumerate another plant's hardware one request at a time.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CameraNonEnumerationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>T018 — the read.</summary>
    [Fact]
    public async Task Reading_another_fabs_camera_is_byte_identical_to_reading_one_that_never_existed()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich);

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage crossFab = await dresden.GetAsync($"/cameras/{inMunich}");
        HttpResponseMessage neverExisted = await dresden.GetAsync($"/cameras/{Guid.CreateVersion7()}");

        await BothRefusalsMustMatchAsync(crossFab, neverExisted, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// T019 — the edit. Where this regresses: a correction has four more ways to
    /// fail than a read, and every one of them is a chance to answer something
    /// more specific about a camera the caller is not allowed to know exists.
    /// </summary>
    [Fact]
    public async Task Correcting_another_fabs_camera_is_byte_identical_to_correcting_one_that_never_existed()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich);

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage crossFab = await PatchAsync(dresden, inMunich, expectedVersion: 0);
        HttpResponseMessage neverExisted = await PatchAsync(dresden, Guid.CreateVersion7(), expectedVersion: 0);

        await BothRefusalsMustMatchAsync(crossFab, neverExisted, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// T020 — the missing-precondition case, and the one where the task list's
    /// stated reasoning turned out to be wrong in a way worth recording.
    ///
    /// <para>
    /// tasks.md predicted this must answer <b>404</b>, on the grounds that a
    /// <c>428 IF_MATCH_REQUIRED</c> for another fab's camera would confirm that
    /// camera exists. That is true only if the 428 is issued <em>after</em> the
    /// camera is looked up. It is not: the endpoint validates the header
    /// immediately after resolving the caller's own fab and before any lookup,
    /// so <b>every</b> identifier gets the same 428 — one that exists in Munich,
    /// one that never existed anywhere. Uniform, and therefore not an oracle.
    /// </para>
    ///
    /// <para>
    /// So the property to assert is indistinguishability, which is what FR-006
    /// actually asks for, rather than a particular status. Forcing a 404 here
    /// would mean loading the camera before validating the precondition — more
    /// work for a malformed request, and a divergence from Automation,
    /// EventIngestion and Identity, which all validate the header at this point.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_correction_with_no_If_Match_is_refused_identically_whichever_camera_it_names()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich);

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpRequestMessage crossFab = new(HttpMethod.Patch, $"/cameras/{inMunich}") { Content = Body() };
        HttpRequestMessage neverExisted =
            new(HttpMethod.Patch, $"/cameras/{Guid.CreateVersion7()}") { Content = Body() };

        HttpResponseMessage crossFabResponse = await dresden.SendAsync(crossFab);
        HttpResponseMessage unknownResponse = await dresden.SendAsync(neverExisted);

        // The status is the same for both, so it says nothing about which
        // cameras exist — that is the guarantee, not the number itself.
        await BothRefusalsMustMatchAsync(
            crossFabResponse, unknownResponse, HttpStatusCode.PreconditionRequired);
    }

    /// <summary>
    /// The other half of the same property, and the one that would actually
    /// leak if the ordering drifted: <em>with</em> a well-formed precondition,
    /// the camera is looked up, so the refusal has to stay uniform there too.
    /// </summary>
    [Fact]
    public async Task A_correction_with_a_valid_If_Match_is_refused_identically_whichever_camera_it_names()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich);

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage crossFab = await PatchAsync(dresden, inMunich, expectedVersion: 1);
        HttpResponseMessage neverExisted = await PatchAsync(dresden, Guid.CreateVersion7(), expectedVersion: 1);

        // Not 412, even though the version is certainly wrong for a camera that
        // does not exist: the camera is refused before its version is compared,
        // so a stale-version answer cannot be used to probe for existence.
        await BothRefusalsMustMatchAsync(crossFab, neverExisted, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// T021. Without this the refusals above could be a blanket denial rather
    /// than fab scoping, and every other test in this file would still pass.
    /// </summary>
    [Fact]
    public async Task An_operator_holding_both_fabs_can_read_and_correct_the_camera()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich);

        using HttpClient multiFab = await ClientFor(MultiFabOperator);

        HttpResponseMessage read = await multiFab.GetAsync($"/cameras/{inMunich}?fabId=munich");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await read.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fab").GetString().ShouldBe("munich");

        HttpResponseMessage corrected = await PatchAsync(
            multiFab, inMunich, body.GetProperty("version").GetInt32(), fabId: "munich");

        corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The comparison SC-003 asks for: same status, and the same problem body
    /// field for field.
    ///
    /// <para>
    /// Two classes of field are normalised rather than compared, and the
    /// distinction matters. Trace-correlation fields are per-request by design.
    /// <b>Identifiers are the caller's own input echoed back</b> — the two
    /// requests necessarily name different cameras, so a literal comparison
    /// could never pass, and reflecting what was asked about tells the caller
    /// only what they already knew.
    /// </para>
    ///
    /// <para>
    /// Everything else is compared exactly, so this still fails on the things
    /// that would be a leak: an extra field, a different title or status, or a
    /// detail that mentions the fab, the name, or anything else about a camera
    /// the caller is not entitled to know exists.
    /// </para>
    /// </summary>
    private static async Task BothRefusalsMustMatchAsync(
        HttpResponseMessage crossFab,
        HttpResponseMessage neverExisted,
        HttpStatusCode expected)
    {
        crossFab.StatusCode.ShouldBe(expected);
        neverExisted.StatusCode.ShouldBe(expected);

        Dictionary<string, string> crossFabFields = await ProblemFieldsAsync(crossFab);
        Dictionary<string, string> unknownFields = await ProblemFieldsAsync(neverExisted);

        crossFabFields.ShouldBe(unknownFields);
    }

    private static async Task<Dictionary<string, string>> ProblemFieldsAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.EnumerateObject()
            .Where(field => !string.Equals(field.Name, "traceId", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field.Name, "requestId", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                field => field.Name,
                field => WithoutIdentifiers(field.Value.ToString()),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Replaces any Guid with a placeholder, so an echoed identifier does not
    /// make two otherwise-identical refusals compare unequal.
    /// </summary>
    private static string WithoutIdentifiers(string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            value,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "<identifier>");

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static StringContent Body() =>
        new("{\"rtspUrl\":\"rtsp://10.0.5.77/h264\"}", Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PatchAsync(
        HttpClient cameras, Guid camera, int expectedVersion, string fabId = "")
    {
        string url = fabId.Length == 0 ? $"/cameras/{camera}" : $"/cameras/{camera}?fabId={fabId}";

        HttpRequestMessage request = new(HttpMethod.Patch, url) { Content = Body() };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedVersion}\""));

        return await cameras.SendAsync(request);
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras)
    {
        string name = $"hidden-{Guid.CreateVersion7():N}"[..24];

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
