using System.Text.Json;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// ADR-0189 (issue #3149). The broker ages a dead letter out through a policy in the AppHost's
/// definitions file and Postgres ages it out through <c>WolverineDefaults.DeadLetterRetention</c>.
/// AppHost references nothing of ServiceDefaults, so the two periods are held together by this
/// test rather than by a shared constant.
///
/// <para>
/// A missing definitions file or a missing constant is reported as an assertion about the
/// absent policy, not as an unhandled exception, so the red is the behaviour that is missing.
/// </para>
/// </summary>
public sealed class DeadLetterRetentionAgreementTests
{
    private const string DefinitionsPath = "src/AppHost/Resources/rabbitmq/definitions.json";
    private const string DeadLetterQueuePattern = "^wolverine-dead-letter-queue$";

    [Fact]
    public void The_broker_policy_and_the_postgres_retention_name_the_same_period()
    {
        var brokerTimeToLive = ReadBrokerTimeToLive();
        var retention = ReadRetention();

        brokerTimeToLive.ShouldNotBeNull("the definitions file carries no dead-letter policy with a message-ttl");
        retention.ShouldNotBeNull("WolverineDefaults declares no DeadLetterRetention");
        retention.Value.TotalMilliseconds.ShouldBe(brokerTimeToLive.Value);
    }

    [Fact]
    public void The_retention_is_fourteen_days()
    {
        var retention = ReadRetention();

        retention.ShouldNotBeNull("WolverineDefaults declares no DeadLetterRetention");
        retention.Value.ShouldBe(TimeSpan.FromDays(14));
    }

    [Fact]
    public void The_broker_policy_targets_only_the_dead_letter_queue_and_lasts_fourteen_days()
    {
        var policy = ReadPolicy();

        policy.ShouldNotBeNull("the definitions file carries no dead-letter policy");
        policy.Value.GetProperty("pattern").GetString().ShouldBe(DeadLetterQueuePattern);
        policy.Value.GetProperty("apply-to").GetString().ShouldBe("queues");
        policy.Value.GetProperty("definition").GetProperty("message-ttl").GetInt64()
            .ShouldBe((long)TimeSpan.FromDays(14).TotalMilliseconds);
    }

    private static TimeSpan? ReadRetention()
    {
        var field = typeof(WolverineDefaults).GetField("DeadLetterRetention");
        return field?.GetValue(null) as TimeSpan?;
    }

    private static long? ReadBrokerTimeToLive()
    {
        var policy = ReadPolicy();
        return policy?.GetProperty("definition").GetProperty("message-ttl").GetInt64();
    }

    private static JsonElement? ReadPolicy()
    {
        var path = Path.Combine(RepositoryRoot(), DefinitionsPath);
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("policies", out var policies))
        {
            return null;
        }

        foreach (var policy in policies.EnumerateArray())
        {
            if (policy.TryGetProperty("pattern", out var pattern)
                && pattern.GetString() == DeadLetterQueuePattern)
            {
                return policy.Clone();
            }
        }

        return null;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
