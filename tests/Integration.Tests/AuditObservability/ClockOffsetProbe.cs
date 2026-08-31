using System.Data.Common;
using System.Globalization;
using Npgsql;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// How far a process's clock is from the one every service in this system
/// already shares (spec 053 US2).
///
/// <para>
/// <b>Why this exists.</b> The audit pipeline's latency is measured between two
/// timestamps stamped by <i>different processes</i> — one where the change
/// happens, one where the record is written. Three decisions have been reasoned
/// from that number and nobody has established that the two clocks agree. If they
/// disagree by tens of milliseconds then part of the figure is not latency at
/// all, and an attribution built on it would be a confident, specific, wrong
/// answer used to move a requirement.
/// </para>
///
/// <para>
/// <b>Why it is answerable rather than merely boundable.</b> The composition
/// root declares one Postgres server with nine databases, so every service
/// already shares a clock — it had simply never been used as one. Asking that
/// server the time and comparing gives an offset against a common reference, and
/// the difference between two processes' offsets is their relative skew.
/// </para>
/// </summary>
public static class ClockOffsetProbe
{
    /// <summary>
    /// One process's offset from the shared reference, with the uncertainty that
    /// comes with it.
    ///
    /// <para>
    /// The reading is bracketed: the process's clock is read either side of the
    /// round trip and the midpoint is compared with what the server said. The
    /// server's answer arrived somewhere inside that round trip, so **half of it
    /// is the residual** — the amount by which this measurement could itself be
    /// wrong. A figure without that number is not a measurement, and reporting
    /// the offset alone would be exactly the kind of confident-looking claim
    /// this whole story exists to prevent.
    /// </para>
    /// </summary>
    public static async Task<ClockOffset> MeasureAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Opening the connection is not part of the measurement — the first
        // round trip on a fresh connection carries handshake cost that has
        // nothing to do with clock skew, and including it would inflate the
        // residual until the result said nothing.
        await ReadServerTimeAsync(connection, cancellationToken);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset serverTime = await ReadServerTimeAsync(connection, cancellationToken);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        TimeSpan roundTrip = after - before;
        DateTimeOffset localMidpoint = before + (roundTrip / 2);

        return new ClockOffset(
            Offset: serverTime - localMidpoint,
            Residual: roundTrip / 2);
    }

    /// <summary>
    /// The best of several readings, which is the one whose round trip was
    /// shortest.
    ///
    /// <para>
    /// A long round trip does not make the offset wrong; it makes it
    /// <i>uncertain</i>. So the reading to keep is the one that leaves least room
    /// for error, not an average — averaging would fold a noisy sample's
    /// uncertainty into a quiet one's answer.
    /// </para>
    /// </summary>
    public static async Task<ClockOffset> MeasureBestOfAsync(
        string connectionString, int readings, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(readings, 1);

        ClockOffset best = await MeasureAsync(connectionString, cancellationToken);
        for (int reading = 1; reading < readings; reading += 1)
        {
            ClockOffset candidate = await MeasureAsync(connectionString, cancellationToken);
            if (candidate.Residual < best.Residual)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static async Task<DateTimeOffset> ReadServerTimeAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();

        // clock_timestamp(), not now(): now() returns the time the surrounding
        // transaction began, which for a measurement of the clock itself would
        // report when we started asking rather than when it answered.
        command.CommandText = "SELECT clock_timestamp() AT TIME ZONE 'UTC'";

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return new DateTimeOffset((DateTime)value!, TimeSpan.Zero);
    }
}

/// <summary>
/// A clock reading: how far off, and by how much that could itself be wrong.
/// </summary>
public readonly record struct ClockOffset(TimeSpan Offset, TimeSpan Residual)
{
    /// <summary>The furthest the true offset could be from zero, given the residual.</summary>
    public TimeSpan WorstCase => TimeSpan.FromTicks(Math.Abs(Offset.Ticks) + Math.Abs(Residual.Ticks));

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Offset.TotalMilliseconds:F2} ms ± {Residual.TotalMilliseconds:F2} ms");
}

/// <summary>
/// How far apart two processes' clocks are, and whether that is small enough for
/// an attribution taken between them to mean anything.
/// </summary>
public readonly record struct RelativeSkew(TimeSpan Skew, TimeSpan Residual)
{
    /// <summary>
    /// The difference between two processes' offsets from the shared reference.
    ///
    /// <para>
    /// <b>The residuals add.</b> Each reading could be wrong by its own residual
    /// and they are independent, so the uncertainty in the difference is the sum
    /// — not the larger of the two, and not an average. Understating it here
    /// would make a skew look better established than it is.
    /// </para>
    /// </summary>
    public static RelativeSkew Between(ClockOffset first, ClockOffset second) =>
        new(first.Offset - second.Offset, first.Residual + second.Residual);

    /// <summary>The furthest apart the two clocks could actually be.</summary>
    public TimeSpan WorstCase => TimeSpan.FromTicks(Math.Abs(Skew.Ticks) + Math.Abs(Residual.Ticks));

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Skew.TotalMilliseconds:F2} ms ± {Residual.TotalMilliseconds:F2} ms "
        + $"(worst case {WorstCase.TotalMilliseconds:F2} ms)");
}
