using System.Diagnostics;
using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

/// <summary>
/// Coarse benchmark asserting NFR-002 — 100 000 evals of a
/// representative predicate complete in ≤ 1 second on the dev
/// hardware (≈ 10 µs/eval mean). Uses <see cref="Stopwatch"/>
/// directly so the test stays dependency-free.
///
/// <para>
/// The threshold is generous on purpose — CI runners have
/// variable performance and this test asserts the
/// order-of-magnitude expectation, not the tight production
/// envelope.
/// </para>
/// </summary>
public class AelInterpreterBenchmarkTests
{
    [Fact]
    public void Predicate_evaluation_throughput_clears_NFR002()
    {
        const int iterations = 100_000;

        AelExpression expression = AelParser.Parse(AelFixtures.SimplePlcPredicate);
        EvaluationContext context = AelFixtures.ContextFor(AelFixtures.PlcCycleStartContext);

        // Warm-up.
        for (int i = 0; i < 1_000; i++)
        {
            _ = AelInterpreter.Evaluate(expression, context);
        }

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = AelInterpreter.Evaluate(expression, context);
        }
        sw.Stop();

        sw.Elapsed.ShouldBeLessThan(
            TimeSpan.FromSeconds(1),
            customMessage:
                $"100 000 evals took {sw.Elapsed.TotalMilliseconds:F1} ms; " +
                "exceeds the NFR-002 budget of ≤ 1 second (≈ 10 µs/eval).");
    }
}
