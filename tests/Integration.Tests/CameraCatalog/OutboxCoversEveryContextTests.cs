using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 021 T019, SC-004. The defect was found on the event-ingest path because
/// that path is under load and had just been scrutinised. It was never an ingest
/// problem — nine repositories across every context wrote and then announced,
/// with the same window between them — so a fix demonstrated only where it was
/// found would leave the other eight looking deliberate.
///
/// <para>
/// This asserts the part that is <b>per context</b>: that this context's writes
/// go through the outbox at all, into this context's own schema. The part that
/// is a property of the shared seam rather than of any one context — that a
/// rolled-back write discards its captured announcements — is proven once, in
/// <c>OutboxSharesTheWritesFateTests</c>. Proving it nine times would be
/// re-testing the same three lines.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OutboxCoversEveryContextTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string OutboxSchema = "wolverine_camera_catalog";

    [Fact]
    public async Task A_camera_registration_announces_through_this_contexts_own_outbox()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        string name = $"Outbox-{Guid.CreateVersion7():N}"[..20];
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/cameras",
            new { name, rtspUrl = "rtsp://10.0.9.21/h264" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // Drains to zero: the announcement was captured with the write and then
        // delivered. A backlog that never cleared would mean the messages are
        // being written and not released, which looks identical to working until
        // a consumer is asked whether it heard anything.
        long pending = await DrainedAsync(TimeSpan.FromSeconds(30));
        output.WriteLine($"pending messages in {OutboxSchema}: {pending}");

        pending.ShouldBe(
            0,
            "camera-catalog's announcements are not reaching the broker — the write path "
            + "is enrolled in the outbox but nothing is flushing it");
    }

    private async Task<long> DrainedAsync(TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long pending = await PendingMessagesAsync();

        while (pending > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            pending = await PendingMessagesAsync();
        }

        return pending;
    }

    private async Task<long> PendingMessagesAsync()
    {
        await using CameraCatalogDbContext database = await aspire.CreateCameraCatalogDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM {OutboxSchema}.wolverine_outgoing_envelopes")
            .SingleAsync();
    }
}
