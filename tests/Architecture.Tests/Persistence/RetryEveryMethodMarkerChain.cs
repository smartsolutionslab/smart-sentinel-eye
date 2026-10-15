using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

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
    {
        object? current = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);

        while (current is not null)
        {
            if (current.GetType().Name == MarkerTypeName)
            {
                return true;
            }

            current = current.GetType()
                .GetProperty("InnerHandler", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(current);
        }

        return false;
    }
}
