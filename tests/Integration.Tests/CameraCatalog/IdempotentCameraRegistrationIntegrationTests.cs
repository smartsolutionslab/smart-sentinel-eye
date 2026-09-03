using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// ADR-0142 on one of the six creates that return nothing but an identifier
/// (#2042), against the real stack.
///
/// <para>
/// Identity's tests already prove the mechanism. What is new here and only a
/// live database can settle is that the <b>generic</b> store works against a
/// second <c>DbContext</c> and that this context's migration actually created
/// the table — a store that compiled against every <c>DbContext</c> and had a
/// table in only one would fail exactly here and nowhere earlier.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotentCameraRegistrationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    [Fact]
    public async Task A_repeat_carrying_the_same_key_returns_the_original_camera()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string name = NewName();
        string key = $"key-{Guid.CreateVersion7():N}";

        Guid first = await RegisterAsync(cameras, name, key, HttpStatusCode.Created);
        Guid second = await RegisterAsync(cameras, name, key, HttpStatusCode.Created);

        second.ShouldBe(first, "a replay must return the camera the first attempt created, not a second one.");
    }

    /// <summary>
    /// The control for the test above, and the line that keeps the mechanism
    /// opt-in: without a key the endpoint refuses a duplicate name exactly as it
    /// always has.
    /// </summary>
    [Fact]
    public async Task A_repeat_without_a_key_is_still_refused_as_a_duplicate_name()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string name = NewName();

        await RegisterAsync(cameras, name, key: null, HttpStatusCode.Created);

        using HttpResponseMessage repeat = await SendAsync(cameras, name, key: null);

        repeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A key is a way to survive a retry, not a way past the uniqueness rule.
    /// </summary>
    [Fact]
    public async Task A_different_key_for_the_same_name_is_still_a_duplicate()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string name = NewName();

        await RegisterAsync(cameras, name, $"key-{Guid.CreateVersion7():N}", HttpStatusCode.Created);

        using HttpResponseMessage other = await SendAsync(cameras, name, $"key-{Guid.CreateVersion7():N}");

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A refused create must release its key rather than complete it. Otherwise
    /// the caller could never retry the same key after fixing the request — and
    /// worse, a later retry would replay a camera that was never created.
    /// </summary>
    [Fact]
    public async Task A_key_whose_first_attempt_was_refused_can_be_used_again()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = $"key-{Guid.CreateVersion7():N}";

        using HttpResponseMessage refused = await SendAsync(cameras, name: "", key: key);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());

        Guid created = await RegisterAsync(cameras, NewName(), key, HttpStatusCode.Created);

        created.ShouldNotBe(Guid.Empty);
    }

    private static string NewName() => $"cam-{Guid.CreateVersion7():N}";

    private static async Task<Guid> RegisterAsync(
        HttpClient cameras, string name, string? key, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await SendAsync(cameras, name, key);

        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient cameras, string name, string? key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/cameras?fabId={Fab}")
        {
            Content = JsonContent.Create(new { name, rtspUrl = "rtsp://camera.test/stream" }),
        };

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return cameras.SendAsync(request);
    }
}
