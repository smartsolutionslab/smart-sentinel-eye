using System.Security.Claims;
using Moq;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Api.Requests;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /system-variables</c> answers, and in which order it refuses (spec 366,
/// #2931, FR-003), so that splitting <c>Define</c> under ADR-0084's thirty lines cannot change a
/// status, a problem body, or which refusal wins when a request is wrong in two ways.
///
/// <para>
/// The real define handler runs; only the repository behind it is a double. A caller who holds no
/// fab is refused by <see cref="FabAuthorizationException"/> thrown out of the delegate, which the
/// global exception handler maps to 403 in the running service; an in-process drive does not
/// include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class SystemVariableDefineEndpointCharacterisationTests
{
    private const string Define = "/system-variables/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly System.Text.Json.JsonSerializerOptions WebOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static readonly string NameRefusal = InvalidInput("value must not be null or whitespace. (Parameter 'value')");

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_with_the_key_message()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString()), Caller("dresden"), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_body_is_parsed()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString() with { Name = " " }), Caller("dresden"), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task An_invalid_name_is_refused_with_the_name_message()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString() with { Name = " " }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_type_is_refused_with_the_type_message()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString() with { Type = "Decimal" }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("Unknown VariableType 'Decimal'. Expected: String | Number | Boolean. (Parameter 'value')"));
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_with_an_invalid_type_is_refused_on_the_name_first()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString() with { Name = " ", Type = "Decimal" }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task An_initial_value_that_is_not_of_the_declared_type_is_refused_with_the_value_message()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(
            host, Json(ValidString() with { Type = "Number", InitialValue = "abc" }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("'abc' is not a valid invariant-culture decimal for a Number variable. (Parameter 'raw')"));
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_truthy_label_without_a_falsy_one_is_refused_with_the_falsy_label_message()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(
            host,
            Json(new DefineVariableRequest("door_open", "Boolean", null, "On", null)),
            Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("falsyLabel must not be null or whitespace. (Parameter 'falsyLabel')"));
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_body_from_a_caller_with_no_fab_is_refused_as_input_before_the_fab_is_resolved()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString() with { Name = " " }), Caller());

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task A_caller_in_several_fabs_who_names_none_is_refused_with_the_fab_required_problem()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString()), Caller("dresden", "bremen"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(
            $$"""{"type":"{{BadRequestType}}","title":"VARIABLE_FAB_REQUIRED","status":400,"detail":"You are assigned to more than one fab; name the one this applies to with ?fabId=."}""");
        host.Variables.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_cannot_define_a_variable()
    {
        await using var host = new SystemVariablesEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(Post(host, Json(ValidString()), Caller()));

        host.Variables.Verify(variables => variables.Add(It.IsAny<Variable>()), Times.Never);
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_variable_is_defined_in()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(host, Json(ValidString()), Caller("dresden", "bremen"), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.Variables.Verify(variables => variables.Add(It.Is<Variable>(variable => variable.Fab.Value == "bremen")), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_keyed_define_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new SystemVariablesEndpointHost();
        var body = ValidString();

        var response = await Post(host, Json(body), Caller("dresden"), key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /system-variables");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_unkeyed_define_answers_201_with_the_location_by_name_and_never_touches_the_store()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(
            host,
            Json(new DefineVariableRequest("line_speed", "Number", "3.5", null, null)),
            Caller("dresden"));

        response.Status.ShouldBe(201);
        Guid.TryParse(response.Body.Trim('"'), out _).ShouldBeTrue();
        response.Location.ShouldBe("/system-variables/line_speed");
        host.Variables.Verify(
            variables => variables.Add(It.Is<Variable>(variable =>
                variable.Id.Value.ToString() == response.Body.Trim('"')
                && variable.Fab.Value == "dresden"
                && variable.Name.Value == "line_speed"
                && variable.Type == VariableType.Number
                && variable.Value.ToWireString() == "3.5"
                && variable.BooleanLabels == null)),
            Times.Once);
        host.Variables.Verify(variables => variables.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_boolean_definition_carries_its_labels_to_the_repository()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(
            host,
            Json(new DefineVariableRequest("door_open", "Boolean", "true", "Open", "Closed")),
            Caller("dresden"));

        response.Status.ShouldBe(201);
        host.Variables.Verify(
            variables => variables.Add(It.Is<Variable>(variable =>
                variable.Type == VariableType.Boolean
                && variable.Value.ToWireString() == "true"
                && variable.BooleanLabels == BooleanLabels.From("Open", "Closed"))),
            Times.Once);
    }

    [Fact]
    public async Task A_boolean_definition_without_labels_is_answered_400_by_the_handler()
    {
        await using var host = new SystemVariablesEndpointHost();

        var response = await Post(
            host, Json(new DefineVariableRequest("door_open", "Boolean", null, null, null)), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(
            $$"""{"type":"{{BadRequestType}}","title":"VARIABLE_BOOLEAN_LABELS_REQUIRED","status":400,"detail":"BooleanLabels must be supplied for Boolean variables."}""");
        host.Variables.Verify(variables => variables.Add(It.IsAny<Variable>()), Times.Never);
    }

    [Fact]
    public async Task A_name_already_taken_in_the_fab_is_answered_409_from_the_handler()
    {
        await using var host = new SystemVariablesEndpointHost();
        var existing = Variable.Define(
            FabIdentifier.From("dresden"),
            VariableName.From("line_speed"),
            VariableType.String,
            null,
            null,
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Variables
            .Setup(variables => variables.GetByNameAsync(
                FabIdentifier.From("dresden"),
                VariableName.From("line_speed"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Variable>.Some(existing));

        var response = await Post(
            host, Json(new DefineVariableRequest("line_speed", "String", null, null, null)), Caller("dresden"));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"VARIABLE_NAME_TAKEN","status":409,"detail":"A non-archived system variable with the name 'line_speed' already exists in fab 'dresden'."}""");
        host.Variables.Verify(variables => variables.Add(It.IsAny<Variable>()), Times.Never);
    }

    private static string InvalidInput(string detail) =>
        $$"""{"type":"{{BadRequestType}}","title":"VARIABLE_INVALID_INPUT","status":400,"detail":"{{detail}}"}""";

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, WebOptions);

    private static DefineVariableRequest ValidString() => new("line_speed", "String", "fast", null, null);

    private static ClaimsPrincipal Caller(params string[] fabs) => SystemVariablesEndpointHost.Operator(Subject, fabs);

    private static Task<InProcessResponse> Post(
        SystemVariablesEndpointHost host,
        string body,
        ClaimsPrincipal user,
        string? key = null,
        string? query = null) =>
        host.SendAsync(
            "POST",
            Define,
            user,
            new InProcessRequest(
                BodyJson: body,
                Headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key },
                Query: query));
}
