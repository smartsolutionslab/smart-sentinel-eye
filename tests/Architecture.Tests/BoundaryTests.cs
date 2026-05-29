using System.Collections.Frozen;
using System.Reflection;
using NetArchTest.Rules;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Enforces the inter-context isolation rule from ADR-0027 / ADR-0044:
/// no bounded context references another context's projects directly.
/// Documented exceptions live in <see cref="AllowedCrossContext"/> as
/// a data table — adding a new carve-out is a one-line edit there, no
/// code in the predicate to thread through.
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

    /// <summary>
    /// Documented cross-context allow-rules. Keys are
    /// <c>(consumer, layer)</c>; values are the foreign context prefixes
    /// the consumer is allowed to reference from that layer.
    ///
    /// <para>
    /// Today the only entry is the spec 004 bridge:
    /// OverlayDesigner.Application + OverlayDesigner.Infrastructure
    /// reach into LayoutComposition.Domain for
    /// <c>ILayoutLifecycleBroadcaster</c> so the existing
    /// /hubs/layouts SignalR hub fans out overlay lifecycle events
    /// alongside layout events. The Domain + Api layers stay isolated.
    /// </para>
    /// </summary>
    private static readonly FrozenDictionary<(string Consumer, string Layer), string[]> AllowedCrossContext =
        new Dictionary<(string, string), string[]>
        {
            { ("SmartSentinelEye.OverlayDesigner",   "Application"),    ["SmartSentinelEye.LayoutComposition"] },
            { ("SmartSentinelEye.OverlayDesigner",   "Infrastructure"), ["SmartSentinelEye.LayoutComposition"] },
            // Spec 005 bridge: SystemVariables.Application + .Infrastructure
            // reach into LayoutComposition.Domain for ILayoutLifecycleBroadcaster
            // so the same /hubs/layouts hub also carries ResolvedOverlayTextChanged
            // frames.
            { ("SmartSentinelEye.SystemVariables",   "Application"),    ["SmartSentinelEye.LayoutComposition"] },
            { ("SmartSentinelEye.SystemVariables",   "Infrastructure"), ["SmartSentinelEye.LayoutComposition"] },
        }.ToFrozenDictionary();

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

            string[] allowed = AllowedCrossContext.TryGetValue((contextPrefix, layer), out string[]? carved)
                ? carved
                : Array.Empty<string>();
            string[] foreignContexts = [.. AllContexts
                .Where(c => c != contextPrefix)
                .Where(c => !allowed.Contains(c))];

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
    /// T097 — exercises the spec 004 allow-rule positively:
    /// OverlayDesigner.Application's domain-event handlers must depend
    /// on the LayoutComposition.Domain broadcaster abstraction. If a
    /// refactor accidentally removes the bridge, this test fails —
    /// preventing silent drift away from the spec 004 plan.
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
    /// Spec 005 T095 — exercises the second documented allow-rule
    /// positively: SystemVariables.Application's domain-event
    /// handlers must depend on the LayoutComposition.Domain
    /// broadcaster abstraction.
    /// </summary>
    [Fact]
    public void SystemVariables_Application_uses_the_documented_LayoutLifecycleBroadcaster_bridge()
    {
        Assembly application = Assembly.Load("SmartSentinelEye.SystemVariables.Application");
        TestResult result = Types
            .InAssembly(application)
            .That()
            .HaveNameEndingWith("DomainEventHandler")
            .Should()
            .HaveDependencyOn("SmartSentinelEye.LayoutComposition.Domain.Layout.ILayoutLifecycleBroadcaster")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "SystemVariables.Application domain-event handlers must consume " +
            "ILayoutLifecycleBroadcaster (spec 005 plan, second documented allow-rule).");
    }

    /// <summary>
    /// Spec 005 T095 — SystemVariables.Domain must remain free of
    /// SignalR / EF Core / Wolverine / Npgsql refs even though
    /// Application + Infrastructure use them via the bridge.
    /// </summary>
    [Fact]
    public void SystemVariables_Domain_has_no_infrastructure_framework_dependencies()
    {
        Assembly domain = Assembly.Load("SmartSentinelEye.SystemVariables.Domain");
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
            $"SystemVariables.Domain depends on an infrastructure framework: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    /// <summary>
    /// Spec 008 T018 — Identity.Domain must remain free of every
    /// infrastructure framework (SignalR, EF Core, Wolverine,
    /// Npgsql, MQTTnet). The Keycloak Admin REST client lives in
    /// Infrastructure; Domain stays pure.
    /// </summary>
    [Fact]
    public void Identity_Domain_has_no_infrastructure_framework_dependencies()
    {
        Assembly domain = Assembly.Load("SmartSentinelEye.Identity.Domain");
        TestResult result = Types
            .InAssembly(domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Npgsql",
                "MQTTnet")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Identity.Domain depends on an infrastructure framework: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    /// <summary>
    /// Spec 007 T014 — Automation.Domain must remain free of every
    /// infrastructure framework (SignalR, EF Core, Wolverine, Npgsql,
    /// MQTTnet). The AEL expression engine lives in Application; the
    /// rule cache + Wolverine subscriber live in Infrastructure;
    /// Domain stays pure.
    /// </summary>
    [Fact]
    public void Automation_Domain_has_no_infrastructure_framework_dependencies()
    {
        Assembly domain = Assembly.Load("SmartSentinelEye.Automation.Domain");
        TestResult result = Types
            .InAssembly(domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Npgsql",
                "MQTTnet")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Automation.Domain depends on an infrastructure framework: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    /// <summary>
    /// Spec 006 T016 — EventIngestion.Domain must remain free of every
    /// infrastructure framework (SignalR, EF Core, Wolverine, Npgsql,
    /// MQTTnet). The MQTT subscriber + DbContext live in
    /// Infrastructure; Domain stays pure.
    /// </summary>
    [Fact]
    public void EventIngestion_Domain_has_no_infrastructure_framework_dependencies()
    {
        Assembly domain = Assembly.Load("SmartSentinelEye.EventIngestion.Domain");
        TestResult result = Types
            .InAssembly(domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Npgsql",
                "MQTTnet")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"EventIngestion.Domain depends on an infrastructure framework: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
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

    /// <summary>
    /// Spec 008 T090 — the v1 scope catalogue is the only canonical
    /// list of <c>sse.*</c> strings. It must live in
    /// <c>ServiceDefaults</c> so every context's API layer can take
    /// a dependency on it without breaking the boundary rule. A
    /// drift where the catalogue gets duplicated inside any
    /// context's Domain / Application / Infrastructure layer is a
    /// silent split-brain bug — this test catches it.
    /// </summary>
    [Fact]
    public void Scope_catalogue_lives_in_ServiceDefaults()
    {
        Assembly defaults = Assembly.Load("SmartSentinelEye.ServiceDefaults");
        Type? scope = defaults.GetType("SmartSentinelEye.ServiceDefaults.Authorization.Scope");
        Assert.NotNull(scope);

        foreach (string contextPrefix in AllContexts)
        {
            foreach (string layer in new[] { "Domain", "Application", "Infrastructure" })
            {
                string assemblyName = $"{contextPrefix}.{layer}";
                Assembly assembly = Assembly.Load(assemblyName);
                Type[] localScopes = [.. assembly.GetTypes()
                    .Where(t => t.Name == "Scope" && t.Namespace?.EndsWith(".Authorization", StringComparison.Ordinal) == true)];
                Assert.True(
                    localScopes.Length == 0,
                    $"{assemblyName} defines a local Scope catalogue — must consume ServiceDefaults.Authorization.Scope instead.");
            }
        }
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
