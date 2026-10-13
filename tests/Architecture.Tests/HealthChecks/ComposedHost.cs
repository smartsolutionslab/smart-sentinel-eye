using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), plan §3 (option A). Runs a host's real entry point —
/// top-level-statement <c>Program.cs</c>, whose compiler-synthesised
/// <c>Main</c> is <see cref="Assembly.EntryPoint"/> — on a dedicated thread
/// and captures the <see cref="IHost"/> the generic host builds, stopping
/// before <c>Build()</c> returns to the caller so no hosted service starts
/// and no connection opens.
///
/// <para>
/// <b>The mechanism, and why it is undocumented.</b>
/// <c>Microsoft.Extensions.Hosting</c> fires two <see cref="DiagnosticListener"/>
/// events, on a listener named "Microsoft.Extensions.Hosting", during
/// <c>Build()</c>: <c>HostBuilding</c>, whose payload is an
/// <see cref="IHostBuilder"/> adapter — confirmed by direct probe to be
/// <c>HostApplicationBuilder.HostBuilderAdapter</c> for
/// <see cref="WebApplicationBuilder"/>, and a <c>ConfigureServices</c>
/// callback registered on it demonstrably reaches the host being built — and
/// <c>HostBuilt</c>, whose payload is the built <see cref="IHost"/>. This is
/// the same de-facto contract <c>Microsoft.Extensions.HostFactoryResolver.Sources</c>
/// uses internally; that package is not obtainable here (spec §8). It is the
/// same mechanism <c>WebApplicationFactory</c> and EF Core design-time tooling
/// both depend on, and it has been unchanged since .NET 6. None of the event
/// names, the listener name, or the payload types are documented. A future
/// runtime rename makes every case built on this class fail loudly — a
/// capture that never completes, caught by the timeout below and naming the
/// assembly — never vacuously.
/// </para>
/// </summary>
internal static class ComposedHost
{
    private const string ListenerName = "Microsoft.Extensions.Hosting";

    /// <summary>
    /// Captures <paramref name="entryAssemblyName"/>'s host. The caller
    /// disposes the returned <see cref="IHost"/>; it was built but never
    /// started, and disposing an unstarted host is safe.
    /// </summary>
    /// <param name="entryAssemblyName">The host's own entry-point assembly.</param>
    /// <param name="contentRoot">
    /// Passed as <c>--contentRoot</c> so the host loads its own shipped
    /// <c>appsettings.json</c> from its real project directory.
    /// </param>
    /// <param name="configuration">
    /// Placeholder configuration, passed as command-line arguments rather
    /// than through the <c>HostBuilding</c> hook or an environment variable:
    /// some hosts' own extension methods (e.g. <c>AddBearerAuthentication</c>)
    /// read configuration before <c>Build()</c>, and environment variables
    /// are process-wide and would leak into other tests.
    /// </param>
    /// <param name="configureServices">
    /// Invoked on the <c>HostBuilding</c> payload cast to
    /// <see cref="IHostBuilder"/>, so a test can plant a health check through
    /// the capture mechanism itself rather than editing production code.
    /// <c>null</c> for the guard's own cases.
    /// </param>
    /// <param name="timeout">
    /// Bounds the wait for <c>HostBuilt</c>. On expiry the failure names the
    /// assembly rather than hanging the test run.
    /// </param>
    internal static IHost Capture(
        string entryAssemblyName,
        string contentRoot,
        IReadOnlyDictionary<string, string?> configuration,
        Action<IHostBuilder>? configureServices,
        TimeSpan timeout)
    {
        Ensure.That(entryAssemblyName).IsNotNull();
        Ensure.That(contentRoot).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        var entryPoint = ResolveEntryPoint(entryAssemblyName);
        var args = BuildArgs(configuration, contentRoot);
        TaskCompletionSource<IHost> captured = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Thread captureThread = new(() => RunEntryPoint(entryPoint, args, entryAssemblyName, captured))
        {
            IsBackground = true,
            Name = $"ComposedHost.Capture({entryAssemblyName})",
        };

        var subscription = SubscribeToHostingEvents(captureThread, configureServices, captured);

        captureThread.Start();

        try
        {
            return Wait(captured, entryAssemblyName, timeout);
        }
        finally
        {
            // Dispose only once the capture thread has actually finished —
            // captured.Task completed, whether with the built host or with
            // its own exception. On a timeout the task is still pending and
            // the thread may still be running; disposing here would sever
            // the subscription before a late HostBuilt arrives, leaving no
            // observer to throw CaptureCompleteException. Program.cs would
            // then fall through Build() into MapDefaultEndpoints/RunAsync
            // for real, inside this test process (should-fix 2, PR #2872
            // review). Leaving the subscription (and the thread it watches)
            // live is the lesser leak.
            if (captured.Task.IsCompleted)
            {
                subscription.Dispose();
            }
        }
    }

    private static MethodInfo ResolveEntryPoint(string entryAssemblyName)
    {
        var assembly = Assembly.Load(entryAssemblyName);

        return assembly.EntryPoint
            ?? throw new InvalidOperationException($"{entryAssemblyName} has no entry point ({nameof(Assembly.EntryPoint)} is null).");
    }

    private static string[] BuildArgs(IReadOnlyDictionary<string, string?> configuration, string contentRoot)
    {
        List<string> args = [];
        foreach (var entry in configuration)
        {
            args.Add($"--{entry.Key}={entry.Value}");
        }

        args.Add($"--contentRoot={contentRoot}");

        return [.. args];
    }

    /// <summary>
    /// Subscribes to the process-global <see cref="DiagnosticListener.AllListeners"/>
    /// and, once the "Microsoft.Extensions.Hosting" listener appears, to its
    /// events. Every event is ignored unless it arrives on
    /// <paramref name="captureThread"/>, because the listener is shared by the
    /// whole test process (plan §3.2.3); <see cref="HostCaptureCollection"/>
    /// additionally keeps this from running concurrently with itself.
    /// </summary>
    private static CompositeDisposable SubscribeToHostingEvents(
        Thread captureThread, Action<IHostBuilder>? configureServices, TaskCompletionSource<IHost> captured)
    {
        HostingEventObserver observer = new(captureThread, configureServices, captured);
        var outer = DiagnosticListener.AllListeners.Subscribe(observer);

        return new CompositeDisposable(outer, observer);
    }

    private static void RunEntryPoint(
        MethodInfo entryPoint, string[] args, string entryAssemblyName, TaskCompletionSource<IHost> captured)
    {
        try
        {
            var result = entryPoint.Invoke(null, [args]);

            if (result is Task task)
            {
                // Synchronous up to the sentinel: the exception is set on the
                // task before Invoke returns, since no await is reached
                // before Build() runs. GetResult() rethrows it unwrapped.
                task.GetAwaiter().GetResult();
            }

            captured.TrySetException(
                new InvalidOperationException($"{entryAssemblyName}'s entry point returned without building a host."));
        }
        catch (Exception exception)
        {
            var real = Unwrap(exception);
            if (real is CaptureCompleteException)
            {
                return;
            }

            captured.TrySetException(real);
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        while (true)
        {
            var inner = exception switch
            {
                TargetInvocationException { InnerException: { } wrapped } => wrapped,
                AggregateException { InnerException: { } wrapped } => wrapped,
                _ => null,
            };

            if (inner is null)
            {
                return exception;
            }

            exception = inner;
        }
    }

    private static IHost Wait(TaskCompletionSource<IHost> captured, string entryAssemblyName, TimeSpan timeout)
    {
        if (!captured.Task.Wait(timeout))
        {
            throw new TimeoutException(
                $"{entryAssemblyName} did not reach HostBuilt within {timeout}. Its entry point may be hanging before Build().");
        }

        return captured.Task.GetAwaiter().GetResult();
    }

    private sealed class HostingEventObserver(
        Thread captureThread, Action<IHostBuilder>? configureServices, TaskCompletionSource<IHost> captured)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private IDisposable? innerSubscription;

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == ListenerName)
            {
                innerSubscription = value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (Thread.CurrentThread != captureThread)
            {
                return;
            }

            switch (value.Key)
            {
                case "HostBuilding" when configureServices is not null && value.Value is IHostBuilder builder:
                    configureServices(builder);
                    break;
                case "HostBuilt" when value.Value is IHost host:
                    captured.TrySetResult(host);
                    throw new CaptureCompleteException();
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        public void Dispose() => innerSubscription?.Dispose();
    }

    private sealed class CompositeDisposable(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }
}

/// <summary>
/// Thrown from inside the <c>HostBuilt</c> event handler to unwind the entry
/// point's call to <c>Build()</c> before it returns, so <c>Program.cs</c>
/// never reaches <c>MapDefaultEndpoints</c> or <c>RunAsync</c>. Top-level
/// (rather than nested in the internal <see cref="ComposedHost"/>) only so
/// the type's own declared accessibility can be <c>public</c>, as S3871
/// expects of an exception type; nothing outside this assembly can reach it,
/// and nothing outside <see cref="ComposedHost"/> should construct or catch
/// it — it names the one outcome that means capture succeeded.
/// </summary>
public sealed class CaptureCompleteException : Exception;
