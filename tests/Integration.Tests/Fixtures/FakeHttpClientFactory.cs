namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Hands back one prepared <see cref="HttpClient"/> whatever name is asked for
/// (ADR-0054 — hand-written, not a mocking framework).
///
/// <para>
/// Returning the <b>same</b> instance every time is deliberate: it is what makes
/// this double able to catch a provider that disposes the client the factory
/// gave it. Such a provider passes its first mint and fails its second, which is
/// exactly the bug a double handing out a fresh client each call would hide.
/// </para>
/// </summary>
public sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    /// <summary>Every name asked for, in order — one entry per client handed out.</summary>
    public List<string> RequestedNames { get; } = [];

    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return client;
    }
}
