using System.Net;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// Contract for the webhook rotation's three precondition failures.
///
/// <para>
/// Only the rotation carries them. The device and kiosk disables were
/// reviewed out: a disable is terminal and
/// <c>RegisteredClientRepository.GetByClientIdAsync</c> stops returning the
/// row, so their version cannot move while they are still reachable and a
/// stale case there could only ever fire for a version the client never had.
/// </para>
///
/// <para>
/// The fixture client id deliberately ends in no digit. With something like
/// <c>plc-station-4</c>, asserting the message contains "4" is satisfied by
/// the id itself, so a message that dropped the actual version still passes.
/// </para>
/// </summary>
public class RegisteredClientStaleErrorTests
{
    private const string ClientId = "webhook-qa";

    [Fact]
    public void The_stale_case_is_a_409_naming_both_versions()
    {
        ApiError error = new RotateWebhookClientError.WebhookClientStale(ClientId, 3, 4);

        error.Code.ShouldBe("WEBHOOK_CLIENT_STALE");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
        error.Message.ShouldContain(ClientId);
        error.Message.ShouldContain("3");
        error.Message.ShouldContain("4");
    }

    // 412 rather than 409 for the next two: they are not "the resource moved
    // under you" but "the operation you asked for is not the one that applies
    // here", which is the precondition-failed shape.
    [Fact]
    public void Creating_something_that_exists_is_a_412_pointing_at_the_version_to_use()
    {
        ApiError error = new RotateWebhookClientError.WebhookClientAlreadyExists(ClientId, 7);

        error.Code.ShouldBe("WEBHOOK_CLIENT_ALREADY_EXISTS");
        error.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        error.Message.ShouldContain("7");
        error.Message.ShouldContain("If-Match");
    }

    [Fact]
    public void Rotating_something_absent_is_a_412_pointing_at_the_create_header()
    {
        ApiError error = new RotateWebhookClientError.WebhookClientNotFound(ClientId, 3);

        error.Code.ShouldBe("WEBHOOK_CLIENT_NOT_FOUND");
        error.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        error.Message.ShouldContain("If-None-Match");
    }

    [Fact]
    public void The_stale_message_tells_the_caller_to_re_read_rather_than_retry()
    {
        ApiError error = new RotateWebhookClientError.WebhookClientStale(ClientId, 3, 4);

        error.Message.ShouldContain("Re-read");
        error.Message.ShouldNotContain("Try again");
    }
}
