using System.Reflection;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Guards on the run-mode driver itself (spec 054 Phase 2).
///
/// <para>
/// <b>These are cheap, they need no stack, and each one stands in front of a
/// failure that would otherwise be silent.</b> The measurement they protect
/// produces a breakdown that reads identically whether it came from the intended
/// stack or another — so the guards are on the things that decide *which*, not on
/// the numbers.
/// </para>
/// </summary>
public class RunModeDriverTests
{
    /// <summary>
    /// **The mutation this exists for**: giving the run-mode class
    /// <c>[Collection(AspireCollection.Name)]</c>.
    ///
    /// <para>
    /// That attribute is what injects <c>AspireFixture</c>, and the fixture boots
    /// its own stack. A run-mode test that acquired one would drive load at a
    /// fixture, divide the span correctly, and publish the result labelled "run
    /// mode" — complete, well-formed and wrong, with nothing in the output to
    /// betray it.
    /// </para>
    ///
    /// <para>
    /// The mechanism is real but invisible, so it is asserted rather than relied
    /// upon. This is the only guard here whose failure produces a *plausible*
    /// answer rather than an obvious one.
    /// </para>
    /// </summary>
    [Fact]
    public void The_run_mode_test_cannot_acquire_the_fixture()
    {
        CollectionAttribute? collection = typeof(RunModeIngestAttributionTests)
            .GetCustomAttribute<CollectionAttribute>();

        collection.ShouldBeNull(
            "a collection attribute injects AspireFixture, which boots its own stack — the run-mode "
            + "measurement would then report a fixture's figures as run mode's");
    }

    /// <summary>
    /// The run-mode test must also take no fixture through its constructor, which
    /// is the other way one could arrive.
    /// </summary>
    [Fact]
    public void The_run_mode_test_takes_no_fixture_as_a_dependency()
    {
        bool takesFixture = typeof(RunModeIngestAttributionTests)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType.Name.Contains("AspireFixture", StringComparison.Ordinal));

        takesFixture.ShouldBeFalse("taking the fixture would boot a stack regardless of the attribute");
    }

    /// <summary>
    /// **Absent configuration is a refusal, not a default.**
    ///
    /// <para>
    /// A fallback address is the quiet version of the same failure: the run would
    /// measure whatever answered and label it run mode. Asserting <c>None</c>
    /// here is asserting that no such default exists to fall back to.
    /// </para>
    /// </summary>
    [Fact]
    public void Absent_configuration_yields_no_address_rather_than_a_default()
    {
        string? systemVariables = Environment.GetEnvironmentVariable(
            RunModeStackAddress.SystemVariablesVariable);

        try
        {
            Environment.SetEnvironmentVariable(RunModeStackAddress.SystemVariablesVariable, null);

            Option<RunModeStackAddress> configured = RunModeStackAddress.FromEnvironment();

            configured.HasValue.ShouldBeFalse(
                "with no stack configured there must be no address at all; a default would let the "
                + "run measure something else and call it run mode");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                RunModeStackAddress.SystemVariablesVariable, systemVariables);
        }
    }

    /// <summary>
    /// The refusal has to say what it wanted, or whoever hits it is left guessing
    /// at three variable names and an address format.
    /// </summary>
    [Fact]
    public void The_refusal_names_what_it_needed()
    {
        string missing = RunModeStackAddress.Missing;

        missing.ShouldContain(RunModeStackAddress.SystemVariablesVariable);
        missing.ShouldContain(RunModeStackAddress.KeycloakVariable);
        missing.ShouldContain(RunModeStackAddress.AuditDbVariable);
        missing.ShouldContain("will not", Case.Insensitive);
        missing.ShouldContain("proxied", Case.Insensitive);
    }

    /// <summary>
    /// **Both runs read one shape** (spec 054 US2).
    ///
    /// <para>
    /// Two constants that happen to match satisfy a reader and drift the moment
    /// one is edited. This asserts there is one definition to read — that the run
    /// body both callers use takes its counts from <see cref="IngestRunShape"/>
    /// and not from either caller.
    /// </para>
    /// </summary>
    [Fact]
    public void Neither_run_can_supply_a_shape_of_its_own()
    {
        MethodInfo run = typeof(IngestSpanMeasurement)
            .GetMethod(nameof(IngestSpanMeasurement.RunAsync))
            .ShouldNotBeNull();

        // **The mutation this exists for**: giving the two runs separate shape
        // constants. They cannot, because the run body accepts no counts — it
        // reads IngestRunShape and nothing else can tell it otherwise. A numeric
        // parameter here would be the door through which the two runs drift
        // apart, so its absence is the guard.
        string[] numericParameters = run
            .GetParameters()
            .Where(parameter => parameter.ParameterType == typeof(int)
                || parameter.ParameterType == typeof(double)
                || parameter.ParameterType == typeof(long))
            .Select(parameter => parameter.Name!)
            .ToArray();

        numericParameters.ShouldBeEmpty(
            "the run body takes its counts and its rate from IngestRunShape; a numeric parameter "
            + $"would let one caller run a different shape than the other ({string.Join(", ", numericParameters)})");
    }

    [Fact]
    public void The_run_shape_is_one_definition_both_runs_read()
    {
        IngestRunShape.EventsPerWriter.ShouldBe(
            IngestRunShape.MeasuredEvents / IngestRunShape.Writers,
            "every writer sends the same count, so the measured total must divide exactly");

        (IngestRunShape.EventsPerWriter * IngestRunShape.Writers).ShouldBe(
            IngestRunShape.MeasuredEvents,
            "a shape that does not multiply back would drive a different number of events than the "
            + "population every percentile is taken over");

        IngestRunShape.SlotIntervalMs.ShouldBe(
            1000d / IngestRunShape.TargetRatePerSecond,
            0.0001,
            "the pacing interval is derived from the target rate, not set beside it");

        IngestRunShape.MinimumAcceptableRate.ShouldBeLessThan(IngestRunShape.TargetRatePerSecond);
        IngestRunShape.MaximumAcceptableRate.ShouldBeGreaterThan(IngestRunShape.TargetRatePerSecond);
    }

    /// <summary>
    /// The conditions block reports the endpoint, which is the only guard against
    /// attributing a figure to the wrong stack — and the only one no automated
    /// check can replace.
    /// </summary>
    [Fact]
    public void The_conditions_report_the_endpoint_reached()
    {
        IngestRunConditions conditions = new(
            Environment: "run mode (AppHost)",
            Endpoint: "system-variables http://localhost:1234/",
            IntendedRatePerSecond: 100,
            AchievedRatePerSecond: 99,
            LogLevel: "Warning",
            MeasurementSwitchOn: true,
            RowsMeasured: 1_000,
            RowsMissingStamps: 0);

        string described = conditions.Describe();

        described.ShouldContain("http://localhost:1234/");
        described.ShouldContain("run mode (AppHost)");
        described.ShouldContain("endpoint reached");
    }

    /// <summary>
    /// A run below the rate tolerance must be refusable. Asserted on the
    /// condition rather than on the test, so the judgement lives where both runs
    /// read it.
    /// </summary>
    [Theory]
    [InlineData(60, false)]
    [InlineData(84, false)]
    [InlineData(99, true)]
    [InlineData(114, true)]
    [InlineData(140, false)]
    public void A_run_off_the_target_rate_is_not_reportable(double achieved, bool acceptable)
    {
        IngestRunConditions conditions = new(
            Environment: "run mode (AppHost)",
            Endpoint: "somewhere",
            IntendedRatePerSecond: IngestRunShape.TargetRatePerSecond,
            AchievedRatePerSecond: achieved,
            LogLevel: "Warning",
            MeasurementSwitchOn: true,
            RowsMeasured: 1_000,
            RowsMissingStamps: 0);

        conditions.RateWasMet.ShouldBe(acceptable);
    }

    /// <summary>
    /// Verbose logging is a condition of the run, and both directions matter: at
    /// Debug this stack sustains ~80 ev/s, below the rate the requirement names.
    /// </summary>
    [Theory]
    [InlineData("Warning", false)]
    [InlineData("Information", false)]
    [InlineData("Debug", true)]
    [InlineData("Debug (from appsettings)", true)]
    [InlineData("Trace", true)]
    public void Verbose_logging_is_recognised_whatever_it_is_called(string level, bool verbose)
    {
        IngestRunConditions conditions = new(
            Environment: "run mode (AppHost)",
            Endpoint: "somewhere",
            IntendedRatePerSecond: 100,
            AchievedRatePerSecond: 99,
            LogLevel: level,
            MeasurementSwitchOn: true,
            RowsMeasured: 1_000,
            RowsMissingStamps: 0);

        conditions.LoggingIsVerbose.ShouldBe(verbose);
    }
}
