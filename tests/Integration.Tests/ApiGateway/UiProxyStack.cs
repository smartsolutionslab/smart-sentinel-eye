namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Three stub dev servers (shell and the two remotes) plus a stub REST destination, behind a gateway running in
/// <c>Ui:Mode=Proxy</c>. Shared by the gateway tests that need the UI hosted.
/// </summary>
internal sealed class UiProxyStack : IAsyncDisposable
{
    private UiProxyStack(StubDestination shell, StubDestination cameras, StubDestination eventIngestion, StubDestination rest, GatewayTestHost gateway)
    {
        Shell = shell;
        Cameras = cameras;
        EventIngestion = eventIngestion;
        Rest = rest;
        Gateway = gateway;
    }

    public StubDestination Shell { get; }

    public StubDestination Cameras { get; }

    public StubDestination EventIngestion { get; }

    public StubDestination Rest { get; }

    public GatewayTestHost Gateway { get; }

    public static async Task<UiProxyStack> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        var shell = await StubDestination.StartAsync("shell");
        var cameras = await StubDestination.StartAsync("cameras");
        var eventIngestion = await StubDestination.StartAsync("event-ingestion");
        var rest = await StubDestination.StartAsync("rest");
        var gateway = await GatewayTestHost.StartAsync(overrides =>
        {
            GatewayTestHost.PointContextsAt(overrides, rest.Address);
            overrides["Ui:Mode"] = "Proxy";
            overrides["Ui:Proxy:Shell"] = shell.Address.ToString();
            overrides["Ui:Proxy:Cameras"] = cameras.Address.ToString();
            overrides["Ui:Proxy:EventIngestion"] = eventIngestion.Address.ToString();
            configure?.Invoke(overrides);
        });

        return new UiProxyStack(shell, cameras, eventIngestion, rest, gateway);
    }

    public async ValueTask DisposeAsync()
    {
        await Gateway.DisposeAsync();
        await Shell.DisposeAsync();
        await Cameras.DisposeAsync();
        await EventIngestion.DisposeAsync();
        await Rest.DisposeAsync();
    }
}
