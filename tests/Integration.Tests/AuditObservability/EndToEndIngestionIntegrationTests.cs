using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 009 US1 (T055): registering a camera publishes
/// <c>CameraRegisteredV1</c> on the bus; the AuditObservability generic
/// subscriber records exactly one audit row for it, queryable via the read
/// API with the resource pivot resolved (<c>resource_kind = "camera"</c>,
/// <c>resource_identifier = &lt;cameraId&gt;</c>).
///
/// <para>
/// Camera events carry no fab, so the audit row is cross-fab (fab = null).
/// <c>SearchAuditQueryHandler</c> shows those rows only to a caller who
/// belongs to no fab — a caller who holds one is filtered to
/// <c>Fab != null</c> — so this reads as <c>auditor</c>, the account seeded
/// without a fab group for exactly that reason.
/// </para>
///
/// <para>
/// It used to read as <c>operator</c>, which worked only because that account
/// happened to be in no fab either. Spec 013 had to put <c>operator</c> in
/// <c>/fabs/munich</c> so the rule endpoints would accept it, and this test
/// began timing out — the row was there, the caller could no longer see it.
/// The dependency is now deliberate rather than incidental.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EndToEndIngestionIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task Registering_a_camera_produces_one_audit_row_with_the_camera_resource_pivot()
    {
        using HttpClient cameraAdmin = await aspire.CreateAdminClientAsync("camera-catalog");
        string cameraName = $"Audit-E2E-{Guid.CreateVersion7():N}";

        HttpResponseMessage register = await cameraAdmin.PostAsJsonAsync(
            "/cameras",
            new { name = cameraName, rtspUrl = "rtsp://10.0.9.42/h264" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid cameraId = await register.Content.ReadFromJsonAsync<Guid>();
        cameraId.ShouldNotBe(Guid.Empty);

        using HttpClient auditReader = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "auditor", "Auditor1234");

        JsonElement row = await PollForAuditRowAsync(auditReader, cameraId);

        row.GetProperty("eventKind").GetString().ShouldBe("CameraRegisteredV1");
        row.GetProperty("resourceKind").GetString().ShouldBe("camera");
        row.GetProperty("resourceIdentifier").GetString().ShouldBe(cameraId.ToString());
        row.GetProperty("payload").GetString()!.ShouldContain(cameraName);
    }

    private static async Task<JsonElement> PollForAuditRowAsync(HttpClient auditReader, Guid cameraId)
    {
        string query = $"/audit?eventKind=CameraRegisteredV1&resourceIdentifier={cameraId}&pageSize=10";

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage response = await auditReader.GetAsync(query);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            JsonElement rows = page.GetProperty("rows");
            if (rows.GetArrayLength() == 1)
            {
                return rows[0];
            }

            rows.GetArrayLength().ShouldBeLessThanOrEqualTo(1,
                "the unique event_identifier constraint must keep redeliveries to one row");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No audit row for CameraRegisteredV1 / {cameraId} appeared within 20s.");
    }
}
