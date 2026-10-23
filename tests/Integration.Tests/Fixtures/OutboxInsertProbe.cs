using Microsoft.EntityFrameworkCore;

// Every identifier interpolated below is a Guid-derived name this class generated or a schema constant
// a test passed in; none is caller-controlled input, which is what EF1002/EF1003 guard against.
#pragma warning disable EF1002, EF1003

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Spec 442 (ADR-0185), plan test design notes. A durable outgoing row is deleted on
/// acknowledgement within milliseconds, so polling <c>wolverine_outgoing_envelopes</c> for it races
/// the delivery and proves nothing. This installs an <c>AFTER INSERT</c> trigger on a context's
/// outgoing table that copies each inserted row's <c>message_type</c> into a side table, so "a row was
/// committed for this type" can be read afterwards without a race. The trigger runs in the inserting
/// transaction, so a rolled-back publish leaves nothing in the side table either.
/// </summary>
public sealed class OutboxInsertProbe : IAsyncDisposable
{
    private readonly DbContext database;
    private readonly string outboxSchema;
    private readonly string name;

    private OutboxInsertProbe(DbContext database, string outboxSchema, string name)
    {
        this.database = database;
        this.outboxSchema = outboxSchema;
        this.name = name;
    }

    public static async Task<OutboxInsertProbe> InstallAsync(DbContext database, string outboxSchema)
    {
        var name = $"outbox_probe_{Guid.NewGuid():N}";
        OutboxInsertProbe probe = new(database, outboxSchema, name);

        await database.Database.ExecuteSqlRawAsync($"CREATE TABLE public.{name} (message_type text NOT NULL)");
        await database.Database.ExecuteSqlRawAsync(
            $"CREATE FUNCTION public.{name}_fn() RETURNS trigger AS $$ BEGIN "
            + $"INSERT INTO public.{name} (message_type) VALUES (NEW.message_type); RETURN NEW; END $$ LANGUAGE plpgsql");
        await database.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER {name}_trg AFTER INSERT ON {outboxSchema}.wolverine_outgoing_envelopes "
            + $"FOR EACH ROW EXECUTE FUNCTION public.{name}_fn()");

        return probe;
    }

    /// <summary>
    /// Rows committed to the outgoing table whose message type contains <paramref name="messageTypeName"/>.
    /// </summary>
    public async Task<long> CommittedRowsForAsync(string messageTypeName) =>
        await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM public.{name} WHERE message_type LIKE {{0}}",
                $"%{messageTypeName}%")
            .SingleAsync();

    public async ValueTask DisposeAsync()
    {
        await database.Database.ExecuteSqlRawAsync(
            $"DROP TRIGGER IF EXISTS {name}_trg ON {outboxSchema}.wolverine_outgoing_envelopes");
        await database.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS public.{name}_fn()");
        await database.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS public.{name}");
    }
}
