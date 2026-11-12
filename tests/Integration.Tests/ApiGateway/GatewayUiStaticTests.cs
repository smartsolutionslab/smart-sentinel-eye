namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// <c>Ui:Mode=Static</c> serves the built bundles at the same paths as dev: the shell at <c>/</c> with an <c>index.html</c> fallback
/// for deep links, each remote under <c>/remotes/&lt;name&gt;/</c>. Files bypass the rate limiter, carry <c>nosniff</c>, cannot
/// escape their root, and a missing root or entry file stops the gateway at start.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayUiStaticTests
{
    [Fact]
    public async Task Gateway_serves_the_shell_index_at_the_root_and_for_a_deep_link_without_an_extension()
    {
        await using var stack = await StaticUiStack.StartAsync();

        var root = await stack.Gateway.Client.GetStringAsync("/");
        var deepLink = await stack.Gateway.Client.GetStringAsync("/layouts/123");

        root.ShouldContain("shell-index");
        deepLink.ShouldContain("shell-index");
    }

    [Fact]
    public async Task Gateway_serves_a_shell_asset_and_a_missing_asset_is_404_not_the_shell()
    {
        await using var stack = await StaticUiStack.StartAsync();

        var asset = await stack.Gateway.Client.GetStringAsync("/assets/app.js");
        var missing = await stack.Gateway.Client.GetAsync("/assets/missing.js");

        asset.ShouldBe("shell-asset");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/remotes/cameras/remoteEntry.js", "cameras-entry")]
    [InlineData("/remotes/cameras/assets/chunk.js", "cameras-chunk")]
    [InlineData("/remotes/event-ingestion/remoteEntry.js", "event-ingestion-entry")]
    public async Task Gateway_serves_each_remote_from_its_own_root_under_its_prefix(string path, string expected)
    {
        await using var stack = await StaticUiStack.StartAsync();

        var body = await stack.Gateway.Client.GetStringAsync(path);

        body.ShouldBe(expected);
    }

    [Theory]
    [InlineData("/remotes/cameras/nope.js")]
    [InlineData("/remotes/cameras/some/deep/link")]
    [InlineData("/remotes/not-a-remote/remoteEntry.js")]
    [InlineData("/api/not-a-context/things")]
    public async Task Gateway_answers_a_miss_under_remotes_or_api_with_404_and_never_the_shell(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("shell-index");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/layouts/123")]
    [InlineData("/assets/app.js")]
    [InlineData("/remotes/cameras/remoteEntry.js")]
    public async Task Gateway_marks_every_served_file_nosniff(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Theory]
    [InlineData("/remotes/cameras/..%2f..%2fsecret.txt")]
    [InlineData("/..%2fsecret.txt")]
    [InlineData("/remotes/cameras/%2e%2e%2f%2e%2e%2fsecret.txt")]
    public async Task Gateway_does_not_serve_a_file_outside_its_roots(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("outside-the-roots");
    }

    [Fact]
    public async Task Gateway_does_not_rate_limit_static_files()
    {
        await using var stack = await StaticUiStack.StartAsync(overrides => overrides["RateLimiting:PermitLimit"] = "2");

        List<HttpStatusCode> statuses = [];
        for (var attempt = 0; attempt < 8; attempt++)
        {
            statuses.Add((await stack.Gateway.Client.GetAsync("/")).StatusCode);
            statuses.Add((await stack.Gateway.Client.GetAsync("/assets/app.js")).StatusCode);
            statuses.Add((await stack.Gateway.Client.GetAsync("/remotes/cameras/remoteEntry.js")).StatusCode);
        }

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gateway_still_sends_an_api_call_to_the_rest_service_in_static_mode()
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/api/camera-catalog/things");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stack.Rest.Requests.Single().Path.ShouldBe("/things");
    }

    [Fact]
    public async Task Gateway_shows_the_shell_when_a_browser_reloads_a_path_the_shell_shares_with_a_rest_route()
    {
        await using var stack = await StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/system-variables");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).ShouldContain("shell-index");
        stack.Rest.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_shows_the_shell_when_a_browser_reloads_a_deep_link_whose_last_segment_has_a_dot()
    {
        await using var stack = await StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/cameras/cam.01");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("shell-index");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/layouts/123")]
    [InlineData("/index.html")]
    [InlineData("/cameras/cam.01")]
    [InlineData("/remotes/cameras/remoteEntry.js")]
    [InlineData("/remotes/event-ingestion/remoteEntry.js")]
    public async Task Gateway_makes_a_browser_revalidate_every_entry_point(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoCache.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Theory]
    [InlineData("/assets/app.js")]
    [InlineData("/remotes/cameras/assets/chunk.js")]
    public async Task Gateway_leaves_a_hashed_asset_cacheable(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (response.Headers.CacheControl?.NoCache ?? false).ShouldBeFalse();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task Gateway_answers_a_head_navigation_to_a_dotted_deep_link_with_the_shell_headers()
    {
        await using var stack = await StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/cameras/cam.01");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl.ShouldNotBeNull().NoCache.ShouldBeTrue();
    }

    [Fact]
    public async Task Gateway_answers_a_dotted_path_that_is_not_a_page_navigation_with_404()
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/cameras/cam.01");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/remotes/cameras/x.js")]
    [InlineData("/api/x.y")]
    public async Task Gateway_answers_a_page_navigation_under_remotes_or_api_with_404_and_never_the_shell(string path)
    {
        await using var stack = await StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("shell-index");
    }

    [Fact]
    public async Task Gateway_answers_a_post_to_an_unrouted_path_with_404_not_the_shell()
    {
        await using var stack = await StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.PostAsync("/cameras/cam.01", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Gateway_refuses_to_start_when_the_shell_root_does_not_exist()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            StaticUiStack.StartAsync(overrides => overrides["Ui:Static:ShellRoot"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        exception.Message.ShouldContain("Ui:Static:ShellRoot");
    }

    [Theory]
    [InlineData("shell", "index.html", "Ui:Static:ShellRoot")]
    [InlineData("cameras", "remoteEntry.js", "Ui:Static:CamerasRoot")]
    [InlineData("event-ingestion", "remoteEntry.js", "Ui:Static:EventIngestionRoot")]
    public async Task Gateway_refuses_to_start_when_a_root_lacks_its_entry_file(string root, string entryFile, string configurationKey)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            StaticUiStack.StartAsync(removeEntryFile: (root, entryFile)));

        exception.Message.ShouldContain(configurationKey);
        exception.Message.ShouldContain(entryFile);
    }

    [Fact]
    public async Task Gateway_refuses_to_start_in_static_mode_when_a_root_is_not_configured()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            StaticUiStack.StartAsync(overrides => overrides.Remove("Ui:Static:CamerasRoot")));

        exception.Message.ShouldContain("Ui:Static:CamerasRoot");
    }

    /// <summary>Temp-directory builds of the three bundles behind a gateway in <c>Ui:Mode=Static</c>, plus a stub REST destination.</summary>
    private sealed class StaticUiStack : IAsyncDisposable
    {
        private readonly string workingDirectory;

        private StaticUiStack(string workingDirectory, StubDestination rest, GatewayTestHost gateway)
        {
            this.workingDirectory = workingDirectory;
            Rest = rest;
            Gateway = gateway;
        }

        public StubDestination Rest { get; }

        public GatewayTestHost Gateway { get; }

        public static async Task<StaticUiStack> StartAsync(
            Action<Dictionary<string, string?>>? configure = null,
            (string Root, string EntryFile)? removeEntryFile = null)
        {
            var workingDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-static-" + Guid.NewGuid().ToString("N"));
            var shell = Write(workingDirectory, "shell", ("index.html", "<html>shell-index</html>"), ("assets/app.js", "shell-asset"));
            var cameras = Write(workingDirectory, "cameras", ("remoteEntry.js", "cameras-entry"), ("assets/chunk.js", "cameras-chunk"));
            var eventIngestion = Write(workingDirectory, "event-ingestion", ("remoteEntry.js", "event-ingestion-entry"));
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(workingDirectory, "secret.txt"), "outside-the-roots");

            if (removeEntryFile is { } removal)
            {
                System.IO.File.Delete(System.IO.Path.Combine(workingDirectory, removal.Root, removal.EntryFile));
            }

            var rest = await StubDestination.StartAsync("rest");
            try
            {
                var gateway = await GatewayTestHost.StartAsync(overrides =>
                {
                    GatewayTestHost.PointContextsAt(overrides, rest.Address);
                    overrides["Ui:Mode"] = "Static";
                    overrides["Ui:Static:ShellRoot"] = shell;
                    overrides["Ui:Static:CamerasRoot"] = cameras;
                    overrides["Ui:Static:EventIngestionRoot"] = eventIngestion;
                    configure?.Invoke(overrides);
                });

                return new StaticUiStack(workingDirectory, rest, gateway);
            }
            catch
            {
                await rest.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Gateway.DisposeAsync();
            await Rest.DisposeAsync();
            System.IO.Directory.Delete(workingDirectory, recursive: true);
        }

        private static string Write(string workingDirectory, string name, params (string Path, string Content)[] files)
        {
            var root = System.IO.Path.Combine(workingDirectory, name);
            foreach (var (path, content) in files)
            {
                var full = System.IO.Path.Combine(root, path);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                System.IO.File.WriteAllText(full, content);
            }

            return root;
        }
    }
}
