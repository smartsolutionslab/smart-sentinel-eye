using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartSentinelEye.Integration.Tests.AuditObservability;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Messaging;

/// <summary>
/// ADR-0189 Decision 1, broker half (issue #3149). The broker ages a dead letter out through a policy
/// on <c>wolverine-dead-letter-queue</c>, not through a queue argument, so an existing broker whose
/// queue was declared without one is not an "inequivalent arg" startup failure.
///
/// <para>
/// The mechanism test takes the repository's own policy definition from <c>dead-letter-policies.json</c>,
/// points it at a throwaway queue and shortens the period to two seconds, so it proves that the
/// shipped policy shape expires messages without waiting 14 days. A missing definitions file is an
/// assertion failure, not a crash. Everything it creates on the shared broker it deletes.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DeadLetterAgeOutPolicyTests(AspireFixture aspire)
{
    private const string DeadLetterQueue = "wolverine-dead-letter-queue";
    private const string DefinitionsPath = "src/AppHost/Resources/rabbitmq/dead-letter-policies.json";

    [Fact]
    public async Task The_dead_letter_queue_has_a_fourteen_day_message_ttl_from_policy()
    {
        var cancellationToken = CancellationToken.None;
        using var broker = await AuditQueueProbe.ClientAsync(aspire, cancellationToken);

        var queue = await broker.GetFromJsonAsync<JsonElement>($"/api/queues/%2F/{DeadLetterQueue}", cancellationToken);

        queue.GetProperty("effective_policy_definition").GetProperty("message-ttl").GetInt64()
            .ShouldBe((long)TimeSpan.FromDays(14).TotalMilliseconds);
    }

    [Fact]
    public async Task The_dead_letter_queue_is_declared_without_a_ttl_argument()
    {
        var cancellationToken = CancellationToken.None;
        using var broker = await AuditQueueProbe.ClientAsync(aspire, cancellationToken);

        var queue = await broker.GetFromJsonAsync<JsonElement>($"/api/queues/%2F/{DeadLetterQueue}", cancellationToken);

        queue.GetProperty("arguments").TryGetProperty("x-message-ttl", out _)
            .ShouldBeFalse("a TTL declared as a queue argument makes an existing broker fail startup as an inequivalent arg");
    }

    [Fact]
    public async Task The_shipped_policy_shape_expires_a_message_after_its_period()
    {
        var cancellationToken = CancellationToken.None;
        var definitions = System.IO.Path.Combine(RepositoryRoot(), DefinitionsPath);
        System.IO.File.Exists(definitions).ShouldBeTrue("the definitions file carries no dead-letter policy: it does not exist");

        var document = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(definitions, cancellationToken));
        var shipped = document?["policies"]?.AsArray()
            .FirstOrDefault(policy => policy?["pattern"]?.GetValue<string>() == $"^{DeadLetterQueue}$");
        shipped.ShouldNotBeNull("the definitions file carries no dead-letter policy");

        var suffix = Guid.NewGuid().ToString("N");
        var queueName = $"dead-letter-ttl-probe-{suffix}";
        var policyName = $"dead-letter-ttl-probe-{suffix}";
        var policy = JsonNode.Parse(shipped.ToJsonString())!;
        policy["pattern"] = $"^{queueName}$";
        policy["definition"]!["message-ttl"] = 2000;
        policy.AsObject().Remove("name");
        policy.AsObject().Remove("vhost");

        using var broker = await AuditQueueProbe.ClientAsync(aspire, cancellationToken);
        try
        {
            (await broker.PutAsync($"/api/policies/%2F/{policyName}", Json(policy), cancellationToken))
                .EnsureSuccessStatusCode();
            (await broker.PutAsync($"/api/queues/%2F/{queueName}", Json(new JsonObject { ["durable"] = true }), cancellationToken))
                .EnsureSuccessStatusCode();
            var publish = new JsonObject
            {
                ["properties"] = new JsonObject(),
                ["routing_key"] = queueName,
                ["payload"] = "probe",
                ["payload_encoding"] = "string",
            };
            (await broker.PostAsync("/api/exchanges/%2F/amq.default/publish", Json(publish), cancellationToken))
                .EnsureSuccessStatusCode();

            var gone = false;
            for (var attempt = 0; attempt < 30 && !gone; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var queue = await broker.GetFromJsonAsync<JsonElement>($"/api/queues/%2F/{queueName}", cancellationToken);
                gone = queue.TryGetProperty("messages", out var messages) && messages.GetInt32() == 0;
            }

            gone.ShouldBeTrue("the message outlived the policy's message-ttl");
        }
        finally
        {
            await broker.DeleteAsync($"/api/queues/%2F/{queueName}", CancellationToken.None);
            await broker.DeleteAsync($"/api/policies/%2F/{policyName}", CancellationToken.None);
        }
    }

    private static StringContent Json(JsonNode node) =>
        new(node.ToJsonString(), Encoding.UTF8, "application/json");

    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
