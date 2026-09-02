using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel.Tests;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Stands in for <c>WebhookIntegrationRepository</c> plus
/// <c>AggregateVersionInterceptor</c>. It reproduces the version bump
/// because omitting it left every Application-layer concurrency assertion
/// running at version 0 — which is also <c>default(int)</c>, so the gate was
/// only ever tested at the one value that cannot distinguish a real
/// comparison from no comparison.
/// </summary>
public sealed class InMemoryWebhookIntegrationRepository : IWebhookIntegrationRepository
{
    private readonly List<WebhookIntegration> _integrations = [];
    private readonly HashSet<Guid> _persisted = [];

    public IReadOnlyList<WebhookIntegration> Integrations => _integrations;

    /// <summary>
    /// Places an integration that already exists in the database, at
    /// <paramref name="version"/>. Distinct from <see cref="Add"/>, which is
    /// the production path for a row being created now: the interceptor does
    /// not bump <c>Added</c> roots, so only a seeded row's next save moves.
    /// </summary>
    public void Seed(WebhookIntegration integration, int version = 0)
    {
        Ensure.That(integration).IsNotNull();

        AggregateVersions.SetTo(integration, version);

        _integrations.Add(integration);
        _persisted.Add(integration.Id.Value);
        integration.ClearPendingEvents();
    }

    public Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        WebhookIntegration? found = _integrations.SingleOrDefault(i => i.Name == name);
        return Task.FromResult(found is null
            ? Option<WebhookIntegration>.None
            : Option<WebhookIntegration>.Some(found));
    }

    public void Add(WebhookIntegration integration)
    {
        Ensure.That(integration).IsNotNull();
        _integrations.Add(integration);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (WebhookIntegration i in _integrations)
        {
            // Mirrors AggregateVersionInterceptor.RequiresBump: an Added root
            // starts at 0 and is not bumped; an already-persisted root with
            // changes is. Pending events stand in for the change tracker's
            // Modified state — every mutator on this aggregate raises one.
            bool wasAlreadyPersisted = !_persisted.Add(i.Id.Value);
            if (wasAlreadyPersisted && i.PendingEvents.Count > 0)
            {
                AggregateVersions.Bump(i);
            }

            i.ClearPendingEvents();
        }

        return Task.CompletedTask;
    }
}
