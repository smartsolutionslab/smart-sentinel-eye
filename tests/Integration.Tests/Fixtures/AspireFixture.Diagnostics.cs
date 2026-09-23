namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    /// <summary>
    /// Shared home for the format the DiagnoseAsync copies used to build independently:
    /// response body, then the resource's recent log output. Other failure-diagnosis
    /// copies (BodyAsync, Diagnose) are tracked separately — see #2294.
    /// </summary>
    public async Task<string> DiagnoseAsync(string resourceName, HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        return $"body: {body}{Environment.NewLine}{resourceName} log:{Environment.NewLine}{RecentLogs(resourceName)}";
    }
}
