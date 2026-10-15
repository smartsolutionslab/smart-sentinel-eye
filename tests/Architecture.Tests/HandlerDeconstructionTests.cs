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

    [Fact]
    public void Real_tree_handlers_in_the_parenthesised_spelling_are_scanned()
    {
        var scan = HandlerDeconstructionScanner.Scan(HandlerDeconstructionScanner.ReadSources());

        scan.CheckedParenthesised.ShouldBeGreaterThan(0, "no handler using the (var a, var b) spelling was reached");
    }

    [Fact]
    public void A_transposition_in_parenthesised_locals_is_reported()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, HandlerWith("(var device, var kind) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
        scan.Failures.ShouldContain(f => f.Contains("position 0 is 'Kind'") && f.Contains("'device'"));
        scan.Failures.ShouldContain(f => f.Contains("position 1 is 'Device'") && f.Contains("'kind'"));
    }

    [Fact]
    public void A_transposition_beside_a_discard_is_reported()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, HandlerWith("(var device, _) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(1);
        scan.Failures.Single().ShouldContain("position 0");
    }

    [Fact]
    public void Correct_parenthesised_locals_are_checked_and_pass()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, HandlerWith("(var kind, var device) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.CheckedParenthesised.ShouldBe(1);
        scan.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void A_parenthesised_deconstruction_spanning_lines_is_checked()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, HandlerWith("(var device,\n            var kind) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
    }

    [Fact]
    public void A_transposition_with_the_var_prefix_is_reported()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, HandlerWith("var (device, kind) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
    }

    [Fact]
    public void A_handler_is_checked_against_its_own_contexts_record()
    {
        var scan = HandlerDeconstructionScanner.Scan(ArchiveCommandInAlphaAndBeta("var (device, kind) = message;"));

        scan.Checked.ShouldBe(1);
        scan.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void A_same_named_record_elsewhere_does_not_mask_a_transposition()
    {
        var scan = HandlerDeconstructionScanner.Scan(ArchiveCommandInAlphaAndBeta("var (kind, device) = message;"));

        scan.Failures.Count.ShouldBe(2);
    }

    [Fact]
    public void A_record_declared_once_outside_the_context_still_resolves()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            ("src/Shared.Contracts/Sample/SampleV1.cs", SampleRecord),
            ("src/Automation/Handlers/H.cs", HandlerWith("var (device, kind) = message;"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
    }

    [Fact]
    public void A_record_name_declared_in_several_other_contexts_is_reported_as_ambiguous()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            ("src/Alpha/Contracts/ArchiveCommand.cs", ArchiveRecord("Kind", "Device")),
            ("src/Beta/Contracts/ArchiveCommand.cs", ArchiveRecord("Device", "Kind")),
            ("src/Gamma/Handlers/H.cs", HandlerWith("var (kind, device) = message;", "ArchiveCommand"))));

        scan.Failures.Count.ShouldBe(1);
        scan.Failures.Single().ShouldContain("several contexts");
    }

    [Theory]
    [InlineData("public Task Handle(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public Task<int> HandleAsync(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public async ValueTask Handle(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public async Task Handle(SampleV1 message)")]
    [InlineData("public static Task Handle(SampleV1 message, CancellationToken cancellationToken)")]
    public void A_transposition_is_reported_whatever_the_handler_signature(string signature)
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, ClassWith(signature + " { var (device, kind) = message; return default; }"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
        scan.Failures.ShouldContain(f => f.Contains("position 0 is 'Kind'") && f.Contains("'device'"));
        scan.Failures.ShouldContain(f => f.Contains("position 1 is 'Device'") && f.Contains("'kind'"));
    }

    [Theory]
    [InlineData("public Task Handle(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public Task<int> HandleAsync(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public async ValueTask Handle(SampleV1 message, CancellationToken cancellationToken)")]
    [InlineData("public async Task Handle(SampleV1 message)")]
    [InlineData("public static Task Handle(SampleV1 message, CancellationToken cancellationToken)")]
    public void A_correct_deconstruction_is_checked_whatever_the_handler_signature(string signature)
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, ClassWith(signature + " { var (kind, device) = message; return default; }"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("public Task Handle(SampleV1 message, CancellationToken cancellationToken) => Task.CompletedTask;")]
    [InlineData("public Task Handle(SampleV1 message, CancellationToken cancellationToken);")]
    public void A_handler_without_a_block_body_does_not_borrow_the_next_members_body(string declaration)
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, ClassWith(declaration + " public void Other(SampleV1 message) { var (device, kind) = message; }"))));

        scan.Checked.ShouldBe(0);
        scan.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Real_tree_non_async_handler_is_scanned()
    {
        var scan = HandlerDeconstructionScanner.Scan(HandlerDeconstructionScanner.ReadSources());

        scan.CheckedSources.ShouldContain("src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs");
    }

    [Fact]
    public void A_comment_between_the_signature_and_its_body_does_not_hide_the_handler()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, ClassWith(
                "public async Task Handle(SampleV1 message, CancellationToken cancellationToken) // why\n"
                + "{ var (device, kind) = message; }"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
    }

    [Fact]
    public void A_parenthesis_inside_a_default_value_does_not_end_the_parameter_list()
    {
        var scan = HandlerDeconstructionScanner.Scan(Sources(
            (AlphaSample, SampleRecord),
            (AlphaHandler, ClassWith(
                "public async Task Handle(SampleV1 message, string s = \")\") { var (device, kind) = message; }"))));

        scan.Checked.ShouldBe(1);
        scan.Failures.Count.ShouldBe(2);
    }

    private const string AlphaSample = "src/Alpha/Contracts/SampleV1.cs";

    private const string AlphaHandler = "src/Alpha/Handlers/H.cs";

    private const string SampleRecord = "public sealed record SampleV1(string Kind, string Device);";

    private static string ArchiveRecord(string first, string second) =>
        $"public sealed record ArchiveCommand(string {first}, string {second});";

    private static string HandlerWith(string statement, string type = "SampleV1") =>
        "public sealed class H { public async Task Handle("
        + type
        + " message, CancellationToken cancellationToken) { "
        + statement
        + " } }";

    private static string ClassWith(string members) =>
        "public sealed class H { " + members + " }";

    private static Dictionary<string, string> Sources(params (string Path, string Text)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Text, StringComparer.Ordinal);

    private static Dictionary<string, string> ArchiveCommandInAlphaAndBeta(string betaStatement) =>
        Sources(
            ("src/Alpha/Contracts/ArchiveCommand.cs", ArchiveRecord("Kind", "Device")),
            ("src/Beta/Contracts/ArchiveCommand.cs", ArchiveRecord("Device", "Kind")),
            ("src/Beta/Handlers/H.cs", HandlerWith(betaStatement, "ArchiveCommand")));
}
