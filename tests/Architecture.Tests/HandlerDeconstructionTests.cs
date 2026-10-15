
namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the handler-destructuring convention (CLAUDE.md "House rules").
///
/// <para>
/// A positional deconstruction binds by position, not by name, so
/// transposing two same-typed fields compiles cleanly and changes
/// behaviour silently. That is not hypothetical: <c>FabEventIngestedV1</c>
/// was once bound as <c>(eventIdentifier, fab, source, kind, …)</c> when
/// the record is <c>(EventIdentifier, Fab, Source, Device, Kind, …)</c>.
/// All three are <c>string</c>, so it built, bound <c>kind</c> to the
/// device, and silenced every automation rule.
/// </para>
///
/// <para>
/// Reads source rather than metadata, because a deconstruction is syntax
/// and leaves no trace in the assembly. Deliberate renames are allowed —
/// a local may be called something other than its field — but a local
/// named after a <em>different</em> field of the same record is rejected,
/// which is exactly the shape a transposition takes — and the one shape
/// the type system cannot see.
/// </para>
/// </summary>
public class HandlerDeconstructionTests
{
    [Fact]
    public void Handler_deconstructions_bind_their_records_fields_in_order()
    {
        var scan = HandlerDeconstructionScanner.Scan(HandlerDeconstructionScanner.ReadSources());

        scan.Checked.ShouldBeGreaterThan(0, "no handler deconstructions were found — the scan is broken, not the code");
        scan.Failures.ShouldBeEmpty(string.Join(Environment.NewLine, scan.Failures));
    }
}
