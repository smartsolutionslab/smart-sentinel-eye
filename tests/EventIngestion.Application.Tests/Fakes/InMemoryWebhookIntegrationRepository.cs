using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

public sealed class InMemoryWebhookIntegrationRepository : IWebhookIntegrationRepository
{
    private readonly List<WebhookIntegration> _integrations = new();

    public IReadOnlyList<WebhookIntegration> Integrations => _integrations;

    public Task<Option<WebhookIntegration>> GetByNameAsync(
        WebhookIntegrationName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        WebhookIntegration? found = _integrations.SingleOrDefault(i => i.Name == name);
        return Task.FromResult(found is null
            ? Option<WebhookIntegration>.None
            : Option<WebhookIntegration>.Some(found));
    }

    public void Add(WebhookIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _integrations.Add(integration);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (WebhookIntegration i in _integrations)
        {
            i.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
