using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 033 T023 — correcting a camera's name over real HTTP (#1850).
///
/// <para>
/// The uniqueness cases are the substance, and they are here rather than only
/// in unit tests for a specific reason: the rule lives in two layers — the
/// application check and
/// <c>ux_cameras_fab_name_normalized_active</c> — and those two have
/// disagreed before. Spec 028 found the repository predicate missing the
/// status filter the index had always had, and every unit test stayed green
/// because the in-memory double was the thing under test. Only a test over
/// real SQL sees both.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RenameCameraIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string DresdenOperatorPassword = SeededCredentials.OpDresden;
    private const string OriginalUrl = "rtsp://10.0.7.12/h264";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// SC-001. The identifier is what the retire-and-re-register workaround
    /// cannot preserve, and preserving it is the whole feature.
    /// </summary>
    [Fact]
    public async Task Renaming_keeps_the_camera_and_its_registration_record()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(cameras, Unique("line-3"));

        var before = await ReadAsync(cameras, camera);
        var registeredAt = before.GetProperty("registeredAt").GetString()!;
        var version = before.GetProperty("version").GetInt32();

        var corrected = Unique("line-4");
        (await RenameAsync(cameras, camera, corrected, version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await ReadAsync(cameras, camera);

        after.GetProperty("name").GetString().ShouldBe(corrected);
        after.GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera);
        after.GetProperty("registeredAt").GetString().ShouldBe(registeredAt);
        after.GetProperty("version").GetInt32().ShouldBeGreaterThan(version);
    }

    /// <summary>
    /// FR-006 over real SQL. The application check refuses first; the partial
    /// unique index is what would have caught it had the check been wrong.
    /// </summary>
    [Fact]
    public async Task Renaming_onto_an_active_cameras_name_in_the_same_fab_is_refused()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var taken = Unique("line-4");
        await RegisterAsync(cameras, taken);

        var camera = await RegisterAsync(cameras, Unique("line-3"));
        var version = await VersionOfAsync(cameras, camera);

        var refused = await RenameAsync(cameras, camera, taken, version);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOfAsync(refused)).ShouldBe("CAMERA_NAME_TAKEN");
    }

    /// <summary>
    /// #1434, through the rename path this time. Asserting only the exact match
    /// above would pass against a case-sensitive comparison — which is the
    /// defect that was found in this predicate once already.
    /// </summary>
    [Fact]
    public async Task Renaming_onto_a_name_differing_only_in_case_is_refused()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var taken = Unique("line-4");
        await RegisterAsync(cameras, taken);

        var camera = await RegisterAsync(cameras, Unique("line-3"));
        var version = await VersionOfAsync(cameras, camera);

        var refused =
            await RenameAsync(cameras, camera, taken.ToUpperInvariant(), version);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOfAsync(refused)).ShouldBe("CAMERA_NAME_TAKEN");
    }

    /// <summary>
    /// FR-011, and the reason it is a test rather than an observation. The
    /// index keys on the current name and filters retired rows, so the old name
    /// is free the moment the rename commits. Spec 028's research read this
    /// same index, concluded a requirement needed no production code, and was
    /// wrong about the layer above it — so the behaviour is asserted, not
    /// inherited.
    /// </summary>
    [Fact]
    public async Task The_name_a_rename_frees_can_be_registered_again()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var original = Unique("line-3");
        var camera = await RegisterAsync(cameras, original);
        var version = await VersionOfAsync(cameras, camera);

        (await RenameAsync(cameras, camera, Unique("line-4"), version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reused = await cameras.PostAsJsonAsync("/cameras", new
        {
            name = original,
            rtspUrl = OriginalUrl,
        });

        reused.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await reused.Content.ReadFromJsonAsync<Guid>()).ShouldNotBe(camera);
    }

    /// <summary>FR-009. Retirement is terminal (spec 028).</summary>
    [Fact]
    public async Task A_retired_camera_cannot_be_renamed()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(cameras, Unique("line-3"));

        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var version = await VersionOfAsync(cameras, camera);

        var refused = await RenameAsync(cameras, camera, Unique("line-4"), version);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOfAsync(refused)).ShouldBe("CAMERA_RETIRED");
    }

    /// <summary>
    /// The two conflicts, over the wire. Both are refusals of the same rename,
    /// and only one is resolved by re-reading — so a caller must be able to
    /// tell them apart from what actually arrives, not from what the handler
    /// returned internally.
    /// </summary>
    [Fact]
    public async Task A_taken_name_and_a_stale_version_arrive_as_different_refusals()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var taken = Unique("line-4");
        await RegisterAsync(cameras, taken);

        var camera = await RegisterAsync(cameras, Unique("line-3"));
        var version = await VersionOfAsync(cameras, camera);

        var nameTaken = await RenameAsync(cameras, camera, taken, version);
        var stale = await RenameAsync(cameras, camera, Unique("line-5"), version + 7);

        nameTaken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var takenTitle = await TitleOfAsync(nameTaken);
        var staleTitle = await TitleOfAsync(stale);

        takenTitle.ShouldBe("CAMERA_NAME_TAKEN");
        staleTitle.ShouldBe("CAMERA_VERSION_STALE");

        // ADR-0119: the suffix identifies a lost update, and a taken name is
        // not one — the caller's version is fine.
        takenTitle.ShouldNotEndWith("_STALE");
    }

    /// <summary>
    /// Spec 029 FR-006, re-checked because a rename adds three new ways to
    /// answer something more specific than "no such camera" —
    /// <c>CAMERA_NAME_TAKEN</c>, <c>CAMERA_RETIRED</c> and the precondition
    /// refusals — and any of them would confirm the camera exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserts <b>sameness</b> rather than a particular status, which is the
    /// property that actually matters and the one specs 029 and 030 test the
    /// same way. A first draft of this test asserted <c>404</c> and failed
    /// against correct code: without <c>If-Match</c> the endpoint answers
    /// <c>428</c> before any camera is looked up, so a caller learns nothing —
    /// the answer is identical for a camera that does not exist. Pinning the
    /// status would have made the endpoint's ordering harder to change for no
    /// gain in safety.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_one_that_does_not_exist()
    {
        using var munich = await ClientFor(MunichOperator, MunichOperatorPassword);
        var real = await RegisterAsync(munich, Unique("line-3"));
        var imaginary = Guid.CreateVersion7();

        using var dresden = await ClientFor(DresdenOperator, DresdenOperatorPassword);

        // With a precondition: both must reach the camera lookup and both must
        // be refused identically.
        var crossFab = await RenameAsync(dresden, real, Unique("line-9"), 1);
        var unknown = await RenameAsync(dresden, imaginary, Unique("line-9"), 1);

        crossFab.StatusCode.ShouldBe(unknown.StatusCode);
        (await TitleOfAsync(crossFab)).ShouldBe(await TitleOfAsync(unknown));
        crossFab.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And without one: the precondition refusal must not distinguish them
        // either, which it does not because it is answered before any camera is
        // read.
        var blindCrossFab = await dresden.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/cameras/{real}") { Content = NameBody(Unique("line-9")) });
        var blindUnknown = await dresden.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/cameras/{imaginary}") { Content = NameBody(Unique("line-9")) });

        blindCrossFab.StatusCode.ShouldBe(blindUnknown.StatusCode);
    }

    /// <summary>
    /// FR-010, and its companion. Renaming to the name the camera already has
    /// succeeds; renaming only the letter case also succeeds and is stored,
    /// which is the pair a short-circuit on "the name is unchanged" gets half
    /// right.
    /// </summary>
    [Fact]
    public async Task Renaming_to_the_same_name_succeeds_and_a_case_only_change_is_stored()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var original = Unique("Line-3");
        var camera = await RegisterAsync(cameras, original);

        var version = await VersionOfAsync(cameras, camera);
        (await RenameAsync(cameras, camera, original, version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadAsync(cameras, camera)).GetProperty("name").GetString().ShouldBe(original);

        version = await VersionOfAsync(cameras, camera);
        var lowered = original.ToLowerInvariant();

        (await RenameAsync(cameras, camera, lowered, version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Stored, not silently discarded as "the same name" — the two normalise
        // identically but differ in what an operator reads.
        (await ReadAsync(cameras, camera)).GetProperty("name").GetString().ShouldBe(lowered);
    }

    /// <summary>Neither field, or both, is a request that cannot be applied.</summary>
    [Fact]
    public async Task A_patch_carrying_both_a_name_and_an_address_is_refused()
    {
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(cameras, Unique("line-3"));
        var version = await VersionOfAsync(cameras, camera);

        HttpRequestMessage both = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = new StringContent(
                $"{{\"name\":\"{Unique("line-4")}\",\"rtspUrl\":\"{OriginalUrl}\"}}",
                Encoding.UTF8,
                "application/json"),
        };
        both.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));

        (await cameras.SendAsync(both)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> ClientFor(string username, string password) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, password);

    private static StringContent NameBody(string name) =>
        new($"{{\"name\":\"{name}\"}}", Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> RenameAsync(
        HttpClient cameras, Guid camera, string name, int expectedVersion)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = NameBody(name),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedVersion}\""));

        return await cameras.SendAsync(request);
    }

    private static async Task<string> TitleOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("title").GetString()!;

    private static async Task<JsonElement> ReadAsync(HttpClient cameras, Guid camera)
    {
        var response = await cameras.GetAsync($"/cameras/{camera}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> VersionOfAsync(HttpClient cameras, Guid camera) =>
        (await ReadAsync(cameras, camera)).GetProperty("version").GetInt32();

    /// <summary>A name no other test in this class can collide with.</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}"[..24];

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string name)
    {
        var created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = OriginalUrl,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
