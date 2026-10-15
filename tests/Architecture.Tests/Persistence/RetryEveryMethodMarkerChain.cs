using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.ServiceDefaults.Resilience;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Reads whether a named client's handler chain carries the marker
/// <c>RetryEveryMethod()</c> adds. The chain is assembled inside the factory and
/// exposed nowhere, so it is walked by reflection, as
/// <c>ResilienceHandlerNestingTests</c> does.
/// </summary>
internal static class RetryEveryMethodMarkerChain
{
    private const string MarkerTypeName = "RetryEveryMethodMarker";

    public static bool Carries(IServiceProvider provider, string clientName)
        => Chain(provider, clientName).Contains(MarkerTypeName);

    public static IReadOnlyList<string> Chain(IServiceProvider provider, string clientName)
    {
        typeof(IdempotentRetry).GetNestedType(MarkerTypeName, BindingFlags.NonPublic).ShouldNotBeNull(
            $"{MarkerTypeName} was renamed or removed; a chain walk would find no marker and pass vacuously.");

        List<string> names = [];
        object? current = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);

        while (current is not null)
        {
            names.Add(current.GetType().Name);

            current = current.GetType()
                .GetProperty("InnerHandler", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(current);
        }

        return names;
    }
}
