using System.Runtime.CompilerServices;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

internal static class HostConfigurationWatchers
{
    // Each in-process host watches its appsettings files with one inotify instance; hundreds of short-lived hosts exhaust
    // the runner's limit of 128 and fail unrelated tests at host build.
    [ModuleInitializer]
    internal static void TurnOffReloadOnChange() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
