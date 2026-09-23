namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    /// <summary>
    /// The one place a failing assertion's message is built: the response body,
    /// then the resource's recent log output, so a red status is diagnosable
    /// from the CI log alone without re-running anything.
    /// </summary>
    public async Task<string> DiagnoseAsync(string resourceName, HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        return $"body: {body}{Environment.NewLine}{resourceName} log:{Environment.NewLine}{RecentLogs(resourceName)}";
    }
}
