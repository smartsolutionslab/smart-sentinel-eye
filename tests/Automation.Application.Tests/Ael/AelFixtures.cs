using System.Text.Json;
using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

/// <summary>
/// Shared AEL test data — representative expression strings + their
/// expected token streams. Re-used by
/// <c>AelLexerTests</c>, <c>AelParserTests</c>,
/// <c>AelInterpreterTests</c>, and <c>AelInterpreterBenchmarkTests</c>.
/// </summary>
internal static class AelFixtures
{
    public const string SimplePlcPredicate =
        "$.payload.cycleTime <= 30 && $.kind == \"PlcCycleStart\"";

    public const string OeeValueExpression =
        "100 - $.payload.cycleTime * 2";

    public const string ContainsExpression =
        "$.payload.note contains \"defect\"";

    public const string PrecedenceExpression =
        "1 + 2 * 3 == 7";

    public const string NestedBoolean =
        "($.payload.x > 10 || $.payload.x < -10) && !$.payload.suppressed";

    /// <summary>Returns a context whose root is the supplied object literal.</summary>
    public static EvaluationContext ContextFor(string json)
    {
        JsonDocument doc = JsonDocument.Parse(json);
        return new EvaluationContext(doc.RootElement);
    }

    public const string PlcCycleStartContext = """
        {
          "source": "plc",
          "kind": "PlcCycleStart",
          "device": "station-4",
          "payload": { "cycleTime": 27, "cycleId": "abc", "note": "minor defect on panel" }
        }
        """;

    public const string SuppressedContext = """
        {
          "source": "plc",
          "kind": "PlcCycleStart",
          "device": "station-4",
          "payload": { "x": 42, "suppressed": true }
        }
        """;
}
