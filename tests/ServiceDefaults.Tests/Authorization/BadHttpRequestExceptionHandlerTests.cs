using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

/// <summary>
/// A request the caller got wrong must not be reported as a server failure
/// (#1312). Minimal APIs raise <see cref="BadHttpRequestException"/> with the
/// right status already on it — a missing required query parameter is a 400 —
/// but it is an exception, so <c>UseExceptionHandler</c> flattened every one
/// of them to 500 and told the caller to retry something that could not
/// succeed.
/// </summary>
public class BadHttpRequestExceptionHandlerTests
{
    private static async Task<(int Status, JsonElement Body, bool Handled)> HandleAsync(Exception exception)
    {
        DefaultHttpContext context = new();
        using MemoryStream captured = new();
        context.Response.Body = captured;

        bool handled = await new BadHttpRequestExceptionHandler()
            .TryHandleAsync(context, exception, CancellationToken.None);

        captured.Position = 0;
        JsonElement body = captured.Length == 0
            ? default
            : await JsonSerializer.DeserializeAsync<JsonElement>(captured);

        return (context.Response.StatusCode, body, handled);
    }

    [Fact]
    public async Task A_missing_required_parameter_is_reported_as_the_400_it_is()
    {
        // The shape minimal APIs throw when a required query parameter is
        // absent — the case that made every such endpoint answer 500.
        BadHttpRequestException missing = new(
            "Required parameter \"string fabId\" was not provided from query string.",
            StatusCodes.Status400BadRequest);

        (int status, JsonElement body, bool handled) = await HandleAsync(missing);

        handled.ShouldBeTrue();
        status.ShouldBe(StatusCodes.Status400BadRequest);
        body.GetProperty("status").GetInt32().ShouldBe(StatusCodes.Status400BadRequest);
        body.GetProperty("title").GetString().ShouldBe(BadHttpRequestExceptionHandler.ErrorCode);
    }

    [Fact]
    public async Task The_detail_names_the_parameter_that_could_not_be_bound()
    {
        // Without this the response says only "invalid", and the caller has to
        // guess which of several query parameters they left out.
        BadHttpRequestException missing = new(
            "Required parameter \"string fabId\" was not provided from query string.",
            StatusCodes.Status400BadRequest);

        (_, JsonElement body, _) = await HandleAsync(missing);

        body.GetProperty("detail").GetString()!.ShouldContain("fabId");
    }

    [Fact]
    public async Task A_status_that_is_not_400_survives()
    {
        // The exception carries its own status, so the handler reports it
        // rather than assuming 400. Nothing produces a 413 today — no
        // MaxRequestBodySize is configured, and an oversized payload is
        // refused by the domain as a 400 — so this is the only thing standing
        // between #597 wiring up a limit and that 413 silently arriving as a
        // 400.
        BadHttpRequestException tooLarge = new(
            "Request body too large.", StatusCodes.Status413PayloadTooLarge);

        (int status, JsonElement body, bool handled) = await HandleAsync(tooLarge);

        handled.ShouldBeTrue();
        status.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        body.GetProperty("status").GetInt32().ShouldBe(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task A_genuine_failure_is_left_alone()
    {
        // Declining is the important half: a real fault must keep reaching the
        // 500 handler, or this would hide server bugs behind a 400.
        (_, _, bool handled) = await HandleAsync(new InvalidOperationException("something broke"));

        handled.ShouldBeFalse();
    }
}
