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

    /// <summary>
    /// T097 — exercises the documented exception positively:
    /// OverlayDesigner.Application has at least one type that depends on
    /// the ILayoutLifecycleBroadcaster abstraction in
    /// LayoutComposition.Domain. If a refactor accidentally removes the
    /// bridge, this test fails — preventing a silent drift away from
    /// the spec 004 plan.
    /// </summary>
    [Fact]
    public void OverlayDesigner_Application_uses_the_documented_LayoutLifecycleBroadcaster_bridge()
    {
        Assembly application = Assembly.Load("SmartSentinelEye.OverlayDesigner.Application");
        TestResult result = Types
            .InAssembly(application)
            .That()
            .HaveNameEndingWith("DomainEventHandler")
            .Should()
            .HaveDependencyOn("SmartSentinelEye.LayoutComposition.Domain.Layout.ILayoutLifecycleBroadcaster")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "OverlayDesigner.Application domain-event handlers must consume " +
            "ILayoutLifecycleBroadcaster (spec 004 plan, documented cross-context exception).");
    }

    /// <summary>
    /// T097 — the OverlayDesigner.Domain layer must remain free of
    /// SignalR / EF Core / Wolverine references even though
    /// Application + Infrastructure use them via the bridge.
    /// </summary>
    [Fact]
    public void OverlayDesigner_Domain_has_no_infrastructure_framework_dependencies()
    {
        Assembly domain = Assembly.Load("SmartSentinelEye.OverlayDesigner.Domain");
        TestResult result = Types
            .InAssembly(domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Npgsql")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"OverlayDesigner.Domain depends on an infrastructure framework: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

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
