using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.AppHost;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Characterises the stack-status watch loop through its public entry point,
/// <see cref="StackStatusReport.Attach"/>, without starting a container: the
/// application is built but never run, <see cref="AfterResourcesCreatedEvent"/>
/// is published by hand and states are pushed through
/// <see cref="ResourceNotificationService"/>. These pass on the code as it is
/// and must pass unmodified after the loop's signature is reshaped (#3028).
/// </summary>
[Trait("Category", "FixtureLogic")]
public class StackStatusReportWatchCharacterisationTests : IAsyncLifetime
{
    private const string HeaderPrefix = "# stack-status v1 ";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

    private readonly string directory =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"stack-status-{Guid.NewGuid():N}");

    public StackStatusReportWatchCharacterisationTests() => System.IO.Directory.CreateDirectory(directory);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Attach_seeds_the_expected_resource_as_not_reported_and_omits_parameters()
    {
        var path = ReportPath();
        await using var app = await BuildAsync();

        StackStatusReport.Attach(app, path);

        var lines = await ReadLinesAsync(path);
        lines[0].ShouldStartWith(HeaderPrefix);
        lines.Skip(1).ShouldBe(["watched\t(not reported)\t"]);
    }

    [Fact]
    public async Task A_running_notification_rewrites_the_report_with_the_running_state()
    {
        var path = ReportPath();
        await using var app = await BuildAsync();
        StackStatusReport.Attach(app, path);
        await StartWatchAsync(app);

        await app.ResourceNotifications.PublishUpdateAsync(
            Resource(app, "watched"),
            snapshot => snapshot with { State = KnownResourceStates.Running });

        var lines = await WaitForBodyAsync(path, ["watched\tRunning\t"]);
        lines[0].ShouldStartWith(HeaderPrefix);
    }

    [Fact]
    public async Task A_finished_notification_with_an_exit_code_writes_the_exit_code()
    {
        var path = ReportPath();
        await using var app = await BuildAsync();
        StackStatusReport.Attach(app, path);
        await StartWatchAsync(app);

        await app.ResourceNotifications.PublishUpdateAsync(
            Resource(app, "watched"),
            snapshot => snapshot with { State = KnownResourceStates.Finished, ExitCode = 3 });

        await WaitForBodyAsync(path, ["watched\tFinished\t3"]);
    }

    [Fact]
    public async Task A_parameter_notification_never_adds_a_line_for_the_parameter()
    {
        var path = ReportPath();
        await using var app = await BuildAsync();
        StackStatusReport.Attach(app, path);
        await StartWatchAsync(app);

        await app.ResourceNotifications.PublishUpdateAsync(
            Resource(app, "Secret"),
            snapshot => snapshot with { State = KnownResourceStates.Running });
        await app.ResourceNotifications.PublishUpdateAsync(
            Resource(app, "watched"),
            snapshot => snapshot with { State = KnownResourceStates.Running });

        var lines = await WaitForBodyAsync(path, ["watched\tRunning\t"]);
        lines.ShouldNotContain(line => line.StartsWith("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_notification_for_one_resource_leaves_a_silent_resource_as_not_reported()
    {
        var path = ReportPath();
        await using var app = await BuildAsync(silentContainer: "idle");
        StackStatusReport.Attach(app, path);
        await StartWatchAsync(app);

        await app.ResourceNotifications.PublishUpdateAsync(
            Resource(app, "watched"),
            snapshot => snapshot with { State = KnownResourceStates.Running });

        await WaitForBodyAsync(path, ["idle\t(not reported)\t", "watched\tRunning\t"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Attach_without_a_path_writes_nothing(string? path)
    {
        await using var app = await BuildAsync();

        StackStatusReport.Attach(app, path);

        System.IO.Directory.GetFileSystemEntries(directory).ShouldBeEmpty();
    }

    private string ReportPath() => System.IO.Path.Combine(directory, "stack-status.txt");

    private static async Task<DistributedApplication> BuildAsync(string? silentContainer = null)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        builder.AddContainer("watched", "example/image");
        if (silentContainer is not null)
        {
            builder.AddContainer(silentContainer, "example/image");
        }

        builder.AddParameter("Secret", secret: true);
        return await Task.FromResult(builder.Build());
    }

    private static IResource Resource(DistributedApplication app, string name) =>
        app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == name);

    private static async Task StartWatchAsync(DistributedApplication app)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await app.Services.GetRequiredService<IDistributedApplicationEventing>()
            .PublishAsync(new AfterResourcesCreatedEvent(app.Services, model));
    }

    private static async Task<string[]> WaitForBodyAsync(string path, string[] expectedBody)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        string[] lines = [];
        while (DateTime.UtcNow < deadline)
        {
            // Settle before each read: the loop replaces the file with File.Move(overwrite),
            // which Windows refuses while any reader has it open, and a refused move ends the watch.
            await Task.Delay(300);
            lines = await ReadLinesAsync(path);
            if (lines.Skip(1).SequenceEqual(expectedBody))
            {
                return lines;
            }
        }

        lines.Skip(1).ShouldBe(expectedBody, "the watch loop never rewrote the report to the expected content");
        return lines;
    }

    // The loop replaces the file with File.Move(overwrite); a reader that does not
    // share Delete makes that move fail on Windows and ends the watch.
    private static async Task<string[]> ReadLinesAsync(string path)
    {
        await using var stream = new System.IO.FileStream(
            path,
            System.IO.FileMode.Open,
            System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
        using System.IO.StreamReader reader = new(stream);
        List<string> lines = [];
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }
}
