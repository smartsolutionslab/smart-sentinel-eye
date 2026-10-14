namespace SmartSentinelEye.AppHost;

internal sealed record StackLanes(
    bool IsRunMode,
    bool IsE2ETests,
    bool IsScenarioSimulatorEnabled,
    bool IsPersistentDevStack)
{
    internal static StackLanes Read(IDistributedApplicationBuilder builder)
    {
        var isRunMode = builder.ExecutionContext.IsRunMode;
        var isE2ETests = bool.TryParse(builder.Configuration["E2ETests"], out var e2e) && e2e;
        // ADR-0111 records the Scenario Simulator as dev-only. `E2ETests` alone never
        // delivered that: the end-to-end job boots with a plain `dotnet run` — run
        // mode, flag unset — so the simulator has started there for as long as it has
        // existed (#2013). It cannot simply join the `E2ETests` gate, because that flag
        // also removes the five Vite apps the Playwright suite drives and spec 056's
        // `fixture-video`. Absent or unparseable means a developer's `aspire run`, the
        // only place it is wanted.
        var isScenarioSimulatorEnabled =
            !bool.TryParse(builder.Configuration["ScenarioSimulator"], out var simulator) || simulator;
        // PersistentStack mirrors ScenarioSimulator's parse shape and fail-open
        // default: absent or unparseable means a developer's stack, the only lane
        // that ever wants persistence. CI's end-to-end job passes
        // PersistentStack=false so the containers, data volumes and pgAdmin it
        // creates do not outlive the job's `kill` (#2297).
        var isPersistentStackEnabled =
            !bool.TryParse(builder.Configuration["PersistentStack"], out var persistent) || persistent;
        var isPersistentDevStack = isRunMode && !isE2ETests && isPersistentStackEnabled;

        return new StackLanes(isRunMode, isE2ETests, isScenarioSimulatorEnabled, isPersistentDevStack);
    }
}
