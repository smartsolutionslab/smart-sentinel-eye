using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 023 T002/T003. Splits the first event's arrival-to-effect into two
/// halves using only observables that already exist — no instrumentation, no
/// production change.
///
/// <para>
/// It cannot say which of announce, decide or apply owns the second half, so it
/// does not satisfy SC-001 on its own and is not meant to. What it does is
/// decide <b>which half to search</b> before any change exists to argue about,
/// and it survives afterwards as the independent cross-check on the spans
/// (T011). An attribution with nothing able to disagree with it is a story.
/// </para>
///
/// <para>
/// <b>Category=Measurement</b>, like spec 020's throughput harness: it reports
/// rather than asserts, and a number that moves is a finding rather than a
/// build failure. CI excludes it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
[Trait("Category", "Measurement")]
public class FirstEventSplitTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Fab = "munich";

    private readonly PlantFloor plant = new(aspire);

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One event on a stack that has not seen one, timed at three points: the
    /// publish returning, the event becoming readable, and the effect landing.
    /// </summary>
    [Fact]
    public async Task Where_the_first_events_seconds_go()
    {
        using var variables = await aspire.CreateAdminClientAsync("system-variables");
        using var rules = await aspire.CreateAdminClientAsync("automation");
        using var events = await aspire.CreateAdminClientAsync("event-ingestion");

        // Three events, because the decay is the clue and one measurement
        // cannot show it (T008 wants the same for the spans).
        for (var round = 1; round <= 3; round++)
        {
            await MeasureAsync(variables, rules, events, round);
        }
    }

    private async Task MeasureAsync(
        HttpClient variables, HttpClient rules, HttpClient events, int round)
    {
        var variable = $"split{Guid.NewGuid():N}"[..16];
        await DefineVariableAsync(variables, variable);
        await ActivateRuleAsync(rules, variable);

        var identifier = Guid.CreateVersion7();
        var payload = PayloadWith(identifier);

        var t0 = DateTimeOffset.UtcNow;
        await plant.PublishRawAsync(payload);

        var stored = await WaitForAsync(
            () => IsStoredAsync(events, identifier), t0);
        var applied = await WaitForAsync(
            () => IsAppliedAsync(variables, variable), t0);

        // Reported as two intervals rather than two timestamps: the question is
        // which half owns the seconds, and a reader should not have to subtract.
        var ingress = stored is null ? "never" : $"{stored.Value.TotalMilliseconds:F0} ms";
        var rest = stored is null || applied is null
            ? "n/a"
            : $"{(applied.Value - stored.Value).TotalMilliseconds:F0} ms";
        var total = applied is null ? "never" : $"{applied.Value.TotalMilliseconds:F0} ms";

        output.WriteLine(
            $"round {round}: ingress+store {ingress} | announce+decide+apply {rest} | total {total}");
    }

    /// <summary>
    /// Polls tightly. The interval is the resolution of the answer, and at a
    /// quarter of a second a 200 ms budget is unmeasurable.
    /// </summary>
    private static async Task<TimeSpan?> WaitForAsync(Func<Task<bool>> observed, DateTimeOffset from)
    {
        var deadline = from + Deadline;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await observed())
            {
                return DateTimeOffset.UtcNow - from;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return null;
    }

    private static async Task<bool> IsStoredAsync(HttpClient events, Guid identifier)
    {
        var fetched = await events.GetAsync($"/events/{identifier}?fabId={Fab}");
        return fetched.IsSuccessStatusCode;
    }

    private static async Task<bool> IsAppliedAsync(HttpClient variables, string name)
    {
        var fetched = await variables.GetAsync($"/system-variables/{name}");
        if (!fetched.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("value", out var value) && value.GetString() == "80";
    }

    private static string PayloadWith(Guid identifier) => JsonSerializer.Serialize(new
    {
        eventId = identifier,
        kind = "PlcCycleStart",
        occurredAt = DateTimeOffset.UtcNow,
        payload = new { cycleTime = 10 },
    });

    private static async Task DefineVariableAsync(HttpClient variables, string name)
    {
        var defined = await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
    }

    private static async Task ActivateRuleAsync(HttpClient rules, string variable)
    {
        var name = $"split{Guid.NewGuid():N}"[..18];

        var created = await rules.PostAsJsonAsync($"/rules?fabId={Fab}", new
        {
            name,
            triggerSource = "plc",
            triggerKind = "PlcCycleStart",
            predicate = "$.payload.cycleTime <= 30",
            actionType = "SetVariableValue",
            variableName = variable,
            valueExpression = "100 - $.payload.cycleTime * 2",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
        });
        created.EnsureSuccessStatusCode();

        var fetched = await rules.GetAsync($"/rules/{name}?fabId={Fab}");
        fetched.EnsureSuccessStatusCode();
        var version = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={Fab}");
        publish.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        (await rules.SendAsync(publish)).EnsureSuccessStatusCode();
    }
}
