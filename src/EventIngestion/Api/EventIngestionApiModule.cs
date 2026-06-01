using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Today the
/// manual ingest endpoint resolves <c>IIngestChannel</c> + <c>IClock</c>
/// from Infrastructure directly; no extra Api-scoped registrations
/// are needed until PR E adds the read endpoints + their query
/// handlers.
/// </summary>
public static class EventIngestionApiModule
{
    public static IServiceCollection AddEventIngestionApi(this IServiceCollection services)
    {
        Ensure.That(services).IsNotNull();
        return services;
    }
}
