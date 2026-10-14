using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on the unmodified
/// <c>AuthorizeWhepCommandHandler</c> before <c>HandleAsync</c> is split into
/// private helpers. The handler decides who may open a WHEP session, so what is
/// pinned is the precedence between its refusals (each test sets up two
/// conditions that would both refuse and names the winner), which port calls
/// happen before each refusal (one shared call log over the validator and the
/// repository), the exact code, message and status of every answer, and the text
/// a log reader sees. Existing tests pin outcomes; few of them can see an order.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the scope check below
/// <c>GetByPathAsync</c> turns
/// <see cref="A_missing_read_scope_is_refused_before_the_stream_is_looked_up"/>
/// red (the call log gains "lookup-path"); moving the blank-token check below
/// <c>ValidateAsync</c> turns
/// <see cref="A_blank_token_is_refused_as_unauthorized_before_the_validator_is_called"/>
/// red (the call log gains "validate"); making the fab comparison
/// case-insensitive turns <see cref="The_fab_is_matched_ordinally"/> red.
/// </para>
///
/// <para>
/// Expected values are literals written from reading the unrefactored handler
/// and its error records. Fab and scope comparisons are pinned as ordinal.
/// </para>
/// </summary>
public class AuthorizeWhepRefusalOrderCharacterisationTests
{
    private const string ReadScope = "sse.streams.read";

    private const string CameraGuid = "0197a1b2-0000-7000-8000-0000000000c1";

    private const string PathText = "cam-0197a1b2-0000-7000-8000-0000000000c1";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly CameraIdentifier Camera =
        CameraIdentifier.From(Guid.Parse(CameraGuid, CultureInfo.InvariantCulture));

    private sealed class Harness
    {
        public Harness()
        {
            Handler = new AuthorizeWhepCommandHandler(
                new RecordingWhepAuthValidator(Validator, Calls),
                new RecordingStreamRepository(Repository, Calls),
                Logger);
        }

        public List<string> Calls { get; } = [];

        public FakeWhepAuthValidator Validator { get; } = new();

        public InMemoryStreamRepository Repository { get; } = new();

        public CapturingLogger<AuthorizeWhepCommandHandler> Logger { get; } = new();

        public AuthorizeWhepCommandHandler Handler { get; }

        public Harness Holding(string subject, params string[] scopes)
        {
            Validator.Subject = Option<WhepAuthSubject>.Some(
                new WhepAuthSubject(subject, scopes, []));
            return this;
        }

        public Harness HoldingFabs(string subject, params string[] fabs)
        {
            Validator.Subject = Option<WhepAuthSubject>.Some(
                new WhepAuthSubject(subject, [ReadScope], fabs));
            return this;
        }

        public async Task<Harness> WithStreamAsync(Domain.Stream.Stream stream)
        {
            Repository.Add(stream);
            await Repository.SaveAsync(CancellationToken.None);
            return this;
        }

        public Task<Result<MediaMtxPath, AuthorizeWhepError>> AuthorizeAsync(
            string bearerToken,
            Option<MediaMtxAction> action,
            Option<ReportedMediaMtxAction> reportedAction) =>
            Handler.HandleAsync(
                new AuthorizeWhepCommand(MediaMtxPath.For(Camera), bearerToken, action, reportedAction),
                CancellationToken.None);

        public Task<Result<MediaMtxPath, AuthorizeWhepError>> ReadAsync(string bearerToken = "token-r1") =>
            AuthorizeAsync(
                bearerToken,
                Option<MediaMtxAction>.Some(MediaMtxAction.Read),
                ReportedMediaMtxAction.TryFrom("read"));
    }

    [Fact]
    public async Task An_absent_action_with_a_blank_token_is_refused_on_the_action_before_the_validator_is_called()
    {
        var harness = new Harness().Holding("subject-a", ReadScope);

        var result = await harness.AuthorizeAsync(
            "  ",
            Option<MediaMtxAction>.None,
            Option<ReportedMediaMtxAction>.None);

        AssertRefused(
            result,
            "WHEP_ACTION_UNKNOWN",
            "The request named no recognised action.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBeEmpty();
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            $"Refused a WHEP request on path {PathText}: MediaMTX sent no action field at all — the field was absent, not merely unfamiliar. Check for MediaMTX version skew first — the broker image is pinned, so compare that pin against the release that moved or dropped the field; every viewer stays refused closed until this build reads it from wherever it went.");
    }

    [Fact]
    public async Task An_unrecognised_action_is_refused_on_the_action_and_the_log_names_what_arrived()
    {
        var harness = new Harness().Holding("subject-a", ReadScope);

        var result = await harness.AuthorizeAsync(
            "token-r1",
            MediaMtxAction.TryFrom("stream"),
            ReportedMediaMtxAction.TryFrom("stream"));

        AssertRefused(
            result,
            "WHEP_ACTION_UNKNOWN",
            "The request named no recognised action.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBeEmpty();
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            $"Refused a WHEP request naming action 'stream' on path {PathText}: this build recognises only read, publish and playback. Check for MediaMTX version skew first — the broker image is pinned, so compare that pin against the release that renamed or replaced this action; every viewer stays refused closed until this build learns it.");
    }

    [Fact]
    public async Task An_api_action_with_a_valid_token_is_refused_before_the_validator_is_called()
    {
        var harness = new Harness().Holding("subject-a", ReadScope);

        var result = await harness.AuthorizeAsync(
            "token-r1",
            Option<MediaMtxAction>.Some(MediaMtxAction.Api),
            ReportedMediaMtxAction.TryFrom("api"));

        AssertRefused(
            result,
            "WHEP_ACTION_NOT_PERMITTED",
            "The requested action is not permitted through this hook.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBeEmpty();
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            $"Refused a WHEP api on path {PathText}: nothing in this product publishes through the external-auth hook, so no token admits this action.");
    }

    [Fact]
    public async Task A_publish_action_with_a_blank_token_is_refused_on_the_action_before_the_validator_is_called()
    {
        var harness = new Harness();

        var result = await harness.AuthorizeAsync(
            "",
            Option<MediaMtxAction>.Some(MediaMtxAction.Publish),
            ReportedMediaMtxAction.TryFrom("publish"));

        AssertRefused(
            result,
            "WHEP_ACTION_NOT_PERMITTED",
            "The requested action is not permitted through this hook.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBeEmpty();
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            $"Refused a WHEP publish on path {PathText}: nothing in this product publishes through the external-auth hook, so no token admits this action.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \t")]
    public async Task A_blank_token_is_refused_as_unauthorized_before_the_validator_is_called(string bearerToken)
    {
        var harness = new Harness().Holding("subject-a", ReadScope);

        var result = await harness.ReadAsync(bearerToken);

        AssertRefused(
            result,
            "WHEP_UNAUTHORIZED",
            "Bearer token is missing, malformed, or expired.",
            HttpStatusCode.Unauthorized);
        harness.Calls.ShouldBeEmpty();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejected_token_is_unauthorized_after_one_validator_call_and_no_stream_lookup()
    {
        var harness = new Harness();
        harness.Validator.Failure = WhepAuthFailure.TokenRejected;

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_UNAUTHORIZED",
            "Bearer token is missing, malformed, or expired.",
            HttpStatusCode.Unauthorized);
        harness.Calls.ShouldBe(["validate"]);
    }

    [Fact]
    public async Task An_unreachable_realm_is_answered_as_the_identity_provider_unavailable_with_no_stream_lookup()
    {
        var harness = new Harness();
        harness.Validator.Failure = WhepAuthFailure.IdentityProviderUnavailable;

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_IDENTITY_PROVIDER_UNAVAILABLE",
            "The identity provider could not be reached; the bearer token was not checked.",
            HttpStatusCode.Unauthorized);
        harness.Calls.ShouldBe(["validate"]);
    }

    [Fact]
    public async Task A_missing_read_scope_is_refused_before_the_stream_is_looked_up()
    {
        var harness = await new Harness()
            .Holding("subject-a", "openid", "sse.cameras.read")
            .WithStreamAsync(AStream("dresden"));

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FORBIDDEN",
            "Bearer token does not grant the sse.streams.read scope.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBe(["validate"]);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_read_scope_is_matched_ordinally()
    {
        var harness = new Harness().Holding("subject-a", "SSE.STREAMS.READ");

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FORBIDDEN",
            "Bearer token does not grant the sse.streams.read scope.",
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stream_in_another_fab_is_refused_after_the_validator_and_the_lookup_and_the_refusal_is_logged()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-a", "munich")
            .WithStreamAsync(AStream("dresden"));

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FAB_NOT_AUTHORIZED",
            "Bearer token does not grant access to this stream's fab.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBe(["validate", "lookup-path"]);
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            $"Refused a WHEP open on path {PathText}: subject subject-a does not hold stream fab dresden. The only refusal in this handler that signals attempted cross-fab access rather than a merely-unscoped or malformed caller.");
    }

    [Fact]
    public async Task The_fab_is_matched_ordinally()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-b", "MUNICH")
            .WithStreamAsync(AStream("munich"));

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FAB_NOT_AUTHORIZED",
            "Bearer token does not grant access to this stream's fab.",
            HttpStatusCode.Forbidden);
        harness.Logger.Entries.ShouldHaveSingleItem().Message.ShouldBe(
            $"Refused a WHEP open on path {PathText}: subject subject-b does not hold stream fab munich. The only refusal in this handler that signals attempted cross-fab access rather than a merely-unscoped or malformed caller.");
    }

    [Fact]
    public async Task An_offline_stream_in_another_fab_answers_the_fab_refusal_and_not_the_offline_one()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-a", "munich")
            .WithStreamAsync(AnOfflineStream("dresden"));

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FAB_NOT_AUTHORIZED",
            "Bearer token does not grant access to this stream's fab.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBe(["validate", "lookup-path"]);
        harness.Logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("stream fab dresden.");
    }

    [Fact]
    public async Task A_stream_with_no_fab_attributed_is_refused_as_a_fab_refusal_with_an_empty_fab_in_the_log()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-c", "munich")
            .WithStreamAsync(AnUnattributedStream());

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_FAB_NOT_AUTHORIZED",
            "Bearer token does not grant access to this stream's fab.",
            HttpStatusCode.Forbidden);
        harness.Logger.Entries.ShouldHaveSingleItem().Message.ShouldBe(
            $"Refused a WHEP open on path {PathText}: subject subject-c does not hold stream fab . The only refusal in this handler that signals attempted cross-fab access rather than a merely-unscoped or malformed caller.");
    }

    [Fact]
    public async Task An_offline_stream_in_the_callers_fab_is_unavailable_and_nothing_is_logged()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-a", "munich")
            .WithStreamAsync(AnOfflineStream("munich"));

        var result = await harness.ReadAsync();

        AssertRefused(
            result,
            "WHEP_STREAM_UNAVAILABLE",
            "The requested stream is offline; cannot open a WHEP session.",
            HttpStatusCode.Forbidden);
        harness.Calls.ShouldBe(["validate", "lookup-path"]);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unregistered_path_with_a_valid_token_is_admitted_with_the_path_after_both_calls()
    {
        var harness = new Harness().HoldingFabs("subject-a", "munich");

        var result = await harness.ReadAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(PathText);
        harness.Calls.ShouldBe(["validate", "lookup-path"]);
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_healthy_stream_in_the_callers_fab_is_admitted_with_the_path()
    {
        var harness = await new Harness()
            .HoldingFabs("subject-a", "berlin", "munich")
            .WithStreamAsync(AStream("munich"));

        var result = await harness.ReadAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(PathText);
        harness.Calls.ShouldBe(["validate", "lookup-path"]);
    }

    private static void AssertRefused(
        Result<MediaMtxPath, AuthorizeWhepError> result,
        string code,
        string message,
        HttpStatusCode status)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Message.ShouldBe(message);
        result.Error.Status.ShouldBe(status);
    }

    private static Domain.Stream.Stream AStream(string fab) =>
        new StreamBuilder()
            .ForCamera(Camera)
            .WithFab(FabIdentifier.From(fab))
            .At(Moment)
            .Build();

    private static Domain.Stream.Stream AnOfflineStream(string fab)
    {
        var stream = AStream(fab);
        stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(Moment));
        stream.ReportDegraded(StreamError.From("degraded-7"), new FixedClock(Moment.AddSeconds(15)));
        stream.ReportOffline(StreamError.From("offline-7"), new FixedClock(Moment.AddMinutes(5)));
        return stream;
    }

    private static Domain.Stream.Stream AnUnattributedStream()
    {
        var stream = AStream("munich");

        typeof(Domain.Stream.Stream)
            .GetProperty(nameof(Domain.Stream.Stream.Fab))!
            .SetValue(stream, null);

        return stream;
    }
}
