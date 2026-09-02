using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 012 T011 — proves ADR-0113's Layer 2 against real Postgres and the
/// real <c>Layout</c> mapping.
///
/// <para>
/// Publishing a revision changes only the <c>layout_revisions</c> row. EF
/// issues no UPDATE against <c>layouts</c> for that on its own, so the
/// root's concurrency token would never reach a WHERE clause — this is the
/// case a version bump that only handles directly-modified roots silently
/// misses.
/// </para>
///
/// <para>
/// Deliberately not a mocked throw. A mock would pass against the broken
/// implementation this work replaces, which is the exact failure mode
/// issue #1154 was filed on.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AggregateVersionConflictIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly OperatorIdentifier Editor = OperatorIdentifier.From(Guid.CreateVersion7());

    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_second_of_two_concurrent_writers_is_rejected()
    {
        Guid layoutIdentifier = await SeedDraftAsync();

        await using LayoutCompositionDbContext first = await VersionedContextAsync();
        await using LayoutCompositionDbContext second = await VersionedContextAsync();

        Layout firstCopy = await LoadAsync(first, layoutIdentifier);
        Layout secondCopy = await LoadAsync(second, layoutIdentifier);

        firstCopy.Publish(LayoutRevisionNumber.From(1), Editor, new SystemClock());
        await first.SaveChangesAsync();

        secondCopy.Publish(LayoutRevisionNumber.From(1), Editor, new SystemClock());

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_first_writer_moves_the_version_off_its_loaded_value()
    {
        Guid layoutIdentifier = await SeedDraftAsync();

        await using LayoutCompositionDbContext writer = await VersionedContextAsync();
        Layout layout = await LoadAsync(writer, layoutIdentifier);
        int loaded = layout.Version;

        layout.Publish(LayoutRevisionNumber.From(1), Editor, new SystemClock());
        await writer.SaveChangesAsync();

        await using LayoutCompositionDbContext reader = await VersionedContextAsync();
        Layout reloaded = await LoadAsync(reader, layoutIdentifier);

        reloaded.Version.Value.ShouldBe(loaded + 1);
    }

    /// <summary>
    /// Documents why the interceptor is load-bearing rather than belt-and-
    /// braces: with the same schema and the same concurrency-token mapping
    /// but no version bump, both writers commit and the first one's work is
    /// silently gone. This is `develop`'s behaviour before spec 012.
    /// </summary>
    [Fact]
    public async Task Without_the_interceptor_both_writers_commit_and_one_update_is_lost()
    {
        Guid layoutIdentifier = await SeedDraftAsync();

        await using LayoutCompositionDbContext first = await PlainContextAsync();
        await using LayoutCompositionDbContext second = await PlainContextAsync();

        Layout firstCopy = await LoadAsync(first, layoutIdentifier);
        Layout secondCopy = await LoadAsync(second, layoutIdentifier);

        firstCopy.Publish(LayoutRevisionNumber.From(1), Editor, new SystemClock());
        await first.SaveChangesAsync();

        secondCopy.Publish(LayoutRevisionNumber.From(1), Editor, new SystemClock());
        await second.SaveChangesAsync();

        await using LayoutCompositionDbContext reader = await PlainContextAsync();
        Layout reloaded = await LoadAsync(reader, layoutIdentifier);
        reloaded.Version.Value.ShouldBe(0);
    }

    private async Task<Guid> SeedDraftAsync()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        string name = $"Cnc-{Guid.NewGuid():N}"[..16];

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", new
        {
            name,
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new { cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire), overlayIdentifier = (Guid?)null, row = 0, col = 0 },
            },
        });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private Task<LayoutCompositionDbContext> VersionedContextAsync() =>
        BuildContextAsync(withVersionBump: true);

    private Task<LayoutCompositionDbContext> PlainContextAsync() =>
        BuildContextAsync(withVersionBump: false);

    private async Task<LayoutCompositionDbContext> BuildContextAsync(bool withVersionBump)
    {
        string connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.LayoutCompositionConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{AspireFixture.LayoutCompositionConnectionName}' was not provisioned by Aspire.");

        DbContextOptionsBuilder<LayoutCompositionDbContext> options = new();
        options.UseNpgsql(connectionString);

        if (withVersionBump)
        {
            options.AddInterceptors(new AggregateVersionInterceptor());
        }

        return new LayoutCompositionDbContext(options.Options);
    }

    private static async Task<Layout> LoadAsync(LayoutCompositionDbContext context, Guid layoutIdentifier)
    {
        LayoutIdentifier identifier = LayoutIdentifier.From(layoutIdentifier);

        return await context.Layouts.FirstAsync(layout => layout.Id == identifier);
    }
}
