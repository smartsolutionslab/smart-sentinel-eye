using System.Text;
using System.Text.Json.Nodes;
using SmartSentinelEye.Integration.Tests.AuditObservability;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Messaging;

/// <summary>
/// ADR-0189 ruling 3, ADR-0154 (issue #3149). A dead letter on the shared queue turns a service's
/// readiness Degraded (still <c>200</c>), and emptying the queue turns it Healthy again. The queue
/// is shared, so a message published here is purged in the <c>finally</c>; nothing else may be left
/// on the broker for a later test's <c>/health</c> to trip over.
/// </summary>
[Collection(AspireCollection.Name)]
public class DeadLetterDepthReadinessTests(AspireFixture aspire)
{
    private const string DeadLetterQueue = "wolverine-dead-letter-queue";

    [Fact]
    public async Task A_message_on_the_dead_letter_queue_degrades_readiness_and_purging_it_restores_healthy()
    {
        var cancellationToken = CancellationToken.None;
        using var broker = await AuditQueueProbe.ClientAsync(aspire, cancellationToken);

        try
        {
            (await ReadinessAsync(cancellationToken)).ShouldBe("Healthy", "the queue must be empty before the probe");

            var publish = new JsonObject
            {
                ["properties"] = new JsonObject(),
                ["routing_key"] = DeadLetterQueue,
                ["payload"] = "dead-letter-depth-probe",
                ["payload_encoding"] = "string",
            };
            (await broker.PostAsync(
                "/api/exchanges/%2F/amq.default/publish",
                new StringContent(publish.ToJsonString(), Encoding.UTF8, "application/json"),
                cancellationToken)).EnsureSuccessStatusCode();

            (await WaitForAsync("Degraded", cancellationToken)).ShouldBe("Degraded");

            (await broker.DeleteAsync($"/api/queues/%2F/{DeadLetterQueue}/contents", cancellationToken))
                .EnsureSuccessStatusCode();

            (await WaitForAsync("Healthy", cancellationToken)).ShouldBe("Healthy");
        }
        finally
        {
            await broker.DeleteAsync($"/api/queues/%2F/{DeadLetterQueue}/contents", CancellationToken.None);
        }
    }

    private async Task<string> WaitForAsync(string expected, CancellationToken cancellationToken)
    {
        var observed = string.Empty;
        for (var attempt = 0; attempt < 30 && observed != expected; attempt++)
        {
            observed = await ReadinessAsync(cancellationToken);
            if (observed != expected)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        return observed;
    }

    private async Task<string> ReadinessAsync(CancellationToken cancellationToken)
    {
        using var response = await aspire.CameraCatalog.GetAsync("/health", cancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue("readiness stays 200 whatever the dead-letter depth (ADR-0154)");
        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
    }
}
