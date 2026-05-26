using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IWhepAuthValidator"/> for handler tests. Configure
/// the response per test by setting <see cref="Subject"/> to a result, or
/// leave it null to simulate a rejected token.
/// </summary>
public sealed class FakeWhepAuthValidator : IWhepAuthValidator
{
    public Option<WhepAuthSubject> Subject { get; set; } = Option<WhepAuthSubject>.None;

    public Task<Option<WhepAuthSubject>> ValidateAsync(string bearerToken, CancellationToken cancellationToken) =>
        Task.FromResult(Subject);
}
