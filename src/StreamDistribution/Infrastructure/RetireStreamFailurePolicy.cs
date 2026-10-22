using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.StreamDistribution.Infrastructure;

/// <summary>
/// Placeholder so the red tests for spec 436 / ADR-0182 compile and fail on their assertions. Applies no rule.
/// </summary>
public sealed class RetireStreamFailurePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
    }
}
