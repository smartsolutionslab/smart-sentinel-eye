using System.Net;
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
// The policy is applied through the management API, not by mounting a definitions file with
// `load_definitions`. Observed on the pinned rabbitmq:4-management-alpine (4.x): a definitions
// file makes the broker log "Will not seed default virtual host and user: have definitions to
// load", and a file that names vhost "/" without declaring it fails boot with "Please create
// virtual host "/" prior to importing definitions" — the AppHost's password never becomes a
// user. Declaring the vhost, user and permissions in the file would put a credential in source.
//
// A PUT of a fixed value is idempotent, so this runs on every boot and also reaches a persistent
// container that already holds the queue, with no `docker rm`.
//
// A failure to apply is logged at Error on the rabbitmq resource's own log, where the dashboard
// shows it next to the broker, and does not throw: a throwing ready-callback blocks every
// WaitFor(rabbitmq) dependent, which would stop the whole dev stack over a retention policy.
// The gate that catches a missing policy is the integration test DeadLetterAgeOutPolicyTests in
// CI; local development is deliberately not blocked.
internal static class RabbitMqDeadLetterPolicy
{
    private const string DefinitionsPath = "Resources/rabbitmq/dead-letter-policies.json";
    private const int Attempts = 60;
    private const int MaximumReasonLength = 300;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    internal static IResourceBuilder<RabbitMQServerResource> WithDeadLetterAgeOutPolicy(
        this IResourceBuilder<RabbitMQServerResource> rabbitmq,
        string appHostDirectory)
    {
        var definitionsPath = Path.Combine(appHostDirectory, DefinitionsPath);

        return rabbitmq.OnResourceReady(async (resource, evt, cancellationToken) =>
        {
            var logger = evt.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);

            var managementUrl = resource.GetEndpoint("management").Url;
            var userName = await resource.UserNameReference.GetValueAsync(cancellationToken)
                ?? throw new InvalidOperationException("the RabbitMQ user name parameter has no value");
            var password = await resource.PasswordParameter.GetValueAsync(cancellationToken)
                ?? throw new InvalidOperationException("the RabbitMQ password parameter has no value");

            using HttpClient client = new() { BaseAddress = new Uri(managementUrl), Timeout = RequestTimeout };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));

            await ApplyAsync(definitionsPath, client, logger, cancellationToken);
        });
    }

    private static async Task ApplyAsync(
        string definitionsPath,
        HttpClient client,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var definitions = JsonNode.Parse(await File.ReadAllTextAsync(definitionsPath, cancellationToken));
        var policies = definitions?["policies"]?.AsArray()
            ?? throw new InvalidOperationException($"{definitionsPath} declares no policies");

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
        var path = $"/api/policies/{vhost}/{Uri.EscapeDataString(name)}";
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
                if (!IsRetryable(response.StatusCode))
                {
                    failure = $"{failure}: {await ReadReasonAsync(response, cancellationToken)}";
                    logger.LogError("RabbitMQ policy {Policy} was rejected and will not be retried: {Failure}", name, failure);
                    return;
                }
            }
            catch (HttpRequestException exception)
            {
                failure = exception.Message;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failure = $"no answer within {RequestTimeout.TotalSeconds:0} seconds";
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        logger.LogError("RabbitMQ policy {Policy} was not applied after {Attempts} attempts: {Failure}", name, Attempts, failure);
    }

    // 404 is retried because the queue may not be declared yet; any other 4xx is a request the
    // broker will keep refusing.
    private static bool IsRetryable(HttpStatusCode statusCode) =>
        (int)statusCode >= 500 || statusCode == HttpStatusCode.NotFound;

    // Only the management API's `reason` field is read, never the whole body or the request.
    private static async Task<string> ReadReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var reason = JsonNode.Parse(text)?["reason"]?.GetValue<string>() ?? "no reason in the response";
            return reason.Length > MaximumReasonLength ? reason[..MaximumReasonLength] : reason;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return "the response carried no readable reason";
        }
    }
}
