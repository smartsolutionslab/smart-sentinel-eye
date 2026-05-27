using System.Reflection;
using NetArchTest.Rules;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Enforces the inter-context isolation rule from ADR-0027 / ADR-0044:
/// no bounded context references another context's projects directly.
/// </summary>
public class BoundaryTests
{
    private static readonly string[] AllContexts =
    [
        "SmartSentinelEye.CameraCatalog",
        "SmartSentinelEye.StreamDistribution",
        "SmartSentinelEye.LayoutComposition",
        "SmartSentinelEye.SystemVariables",
        "SmartSentinelEye.EventIngestion",
        "SmartSentinelEye.OverlayDesigner",
        "SmartSentinelEye.Automation",
        "SmartSentinelEye.Identity",
        "SmartSentinelEye.AuditObservability",
    ];

    [Theory]
    [InlineData("SmartSentinelEye.CameraCatalog")]
    [InlineData("SmartSentinelEye.StreamDistribution")]
    [InlineData("SmartSentinelEye.LayoutComposition")]
    [InlineData("SmartSentinelEye.SystemVariables")]
    [InlineData("SmartSentinelEye.EventIngestion")]
    [InlineData("SmartSentinelEye.OverlayDesigner")]
    [InlineData("SmartSentinelEye.Automation")]
    [InlineData("SmartSentinelEye.Identity")]
    [InlineData("SmartSentinelEye.AuditObservability")]
    public void Context_does_not_reference_other_contexts(string contextPrefix)
    {
        foreach (string layer in new[] { "Domain", "Application", "Infrastructure", "Api" })
        {
            string assemblyName = $"{contextPrefix}.{layer}";
            Assembly assembly = Assembly.Load(assemblyName);

            string[] foreignContexts = [.. AllContexts
                .Where(c => c != contextPrefix)
                .Where(c => !IsDocumentedAllowedDependency(contextPrefix, layer, c))];

            TestResult result = Types
                .InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(foreignContexts)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assemblyName} has forbidden cross-context dependencies: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
        }
    }

    /// <summary>
    /// Single documented cross-context allow-rule (spec 004 plan.md):
    /// OverlayDesigner.Application + OverlayDesigner.Infrastructure
    /// consume <c>LayoutComposition.Domain.ILayoutLifecycleBroadcaster</c>
    /// so the existing /hubs/layouts SignalR hub fans out overlay
    /// lifecycle events alongside layout events. The Domain + Api layers
    /// remain isolated.
    /// </summary>
    private static bool IsDocumentedAllowedDependency(string contextPrefix, string layer, string foreignContext) =>
        contextPrefix == "SmartSentinelEye.OverlayDesigner"
        && (layer == "Application" || layer == "Infrastructure")
        && foreignContext == "SmartSentinelEye.LayoutComposition";

    [Fact]
    public void Domain_layer_does_not_reference_infrastructure_frameworks()
    {
        string[] forbiddenNamespaces =
        [
            "Microsoft.EntityFrameworkCore",
            "Marten",
            "Wolverine",
            "Npgsql",
            "RabbitMQ",
            "Microsoft.AspNetCore",
        ];

        foreach (string contextPrefix in AllContexts)
        {
            string assemblyName = $"{contextPrefix}.Domain";
            Assembly assembly = Assembly.Load(assemblyName);

            TestResult result = Types
                .InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assemblyName} depends on a framework forbidden in the Domain layer: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
        }
    }
}
