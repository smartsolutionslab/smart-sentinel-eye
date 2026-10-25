using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.AppHost;

// ADR-0189 Decision 1, broker half (issue #3149). The dead-letter queue ages out through a
// policy, never a queue argument: a TTL argument makes every existing broker fail startup with
// an inequivalent-arg error.
//
// The policy is applied through the management API, not by mounting definitions.json with
// `load_definitions`. Observed on the pinned rabbitmq:4-management-alpine (4.x): a definitions
// file makes the broker log "Will not seed default virtual host and user: have definitions to
// load", and a file that names vhost "/" without declaring it fails boot with "Please create
// virtual host "/" prior to importing definitions" — the AppHost's password never becomes a
// user. Declaring the vhost, user and permissions in the file would put a credential in source.
//
// A PUT of a fixed value is idempotent, so this runs on every boot and also reaches a persistent
// container that already holds the queue, with no `docker rm`.
internal static class RabbitMqDeadLetterPolicy
{
    private const string DefinitionsPath = "Resources/rabbitmq/definitions.json";
    private const int Attempts = 60;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    internal static IResourceBuilder<RabbitMQServerResource> WithDeadLetterAgeOutPolicy(
        this IResourceBuilder<RabbitMQServerResource> rabbitmq,
        string appHostDirectory)
    {
        var definitionsPath = Path.Combine(appHostDirectory, DefinitionsPath);

        return rabbitmq.OnResourceReady(async (resource, evt, cancellationToken) =>
        {
            var logger = evt.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RabbitMqDeadLetterPolicy).FullName ?? nameof(RabbitMqDeadLetterPolicy));

            var managementUrl = resource.GetEndpoint("management").Url;
            var userName = await resource.UserNameReference.GetValueAsync(cancellationToken);
            var password = await resource.PasswordParameter.GetValueAsync(cancellationToken);

            BrokerAccess access = new(managementUrl, userName ?? "guest", password ?? throw new InvalidOperationException("the RabbitMQ password parameter has no value"));

            await ApplyAsync(definitionsPath, access, logger, cancellationToken);
        });
    }

    private static async Task ApplyAsync(
        string definitionsPath,
        BrokerAccess access,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var definitions = JsonNode.Parse(await File.ReadAllTextAsync(definitionsPath, cancellationToken));
        var policies = definitions?["policies"]?.AsArray()
            ?? throw new InvalidOperationException($"{definitionsPath} declares no policies");

        using HttpClient client = new() { BaseAddress = new Uri(access.ManagementUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{access.UserName}:{access.Password}")));

        foreach (var node in policies)
        {
            await PutWithRetryAsync(client, node!.AsObject(), logger, cancellationToken);
        }
    }

    private static async Task PutWithRetryAsync(
        HttpClient client,
        JsonObject policy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var name = policy["name"]!.GetValue<string>();
        var vhost = Uri.EscapeDataString(policy["vhost"]!.GetValue<string>());
        var path = $"/api/policies/{vhost}/{name}";
        var body = JsonNode.Parse(policy.ToJsonString())!.AsObject();
        body.Remove("name");
        body.Remove("vhost");
        var failure = "no attempt made";
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await client.PutAsync(path, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("RabbitMQ policy {Policy} applied", name);
                    return;
                }

                failure = $"HTTP {(int)response.StatusCode}";
            }
            catch (HttpRequestException exception)
            {
                failure = exception.Message;
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        logger.LogError("RabbitMQ policy {Policy} was not applied after {Attempts} attempts: {Failure}", name, Attempts, failure);
        throw new InvalidOperationException($"RabbitMQ policy {name} was not applied: {failure}");
    }

    private sealed record BrokerAccess(string ManagementUrl, string UserName, string Password);
}
