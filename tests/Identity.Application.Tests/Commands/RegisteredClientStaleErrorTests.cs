using System.Net;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// ADR-0047 gives every command its own error union, so the stale-version
/// case is declared three times.
///
/// <para>
/// Identity is one of the two contexts where these unions live **inline** in
/// the <c>*Command.cs</c> files rather than in a separate <c>*Errors.cs</c>,
/// so a glob over <c>Commands/*Errors.cs</c> misses them. This test names all
/// three explicitly so the set cannot drift apart unnoticed.
/// </para>
///
/// <para>
/// Unlike the other contexts these do **not** share one code: Identity
/// already flavours its codes per command — <c>DEVICE_NOT_FOUND</c> versus
/// <c>KIOSK_NOT_FOUND</c> for the same aggregate — and a caller disabling a
/// kiosk should not be handed a code naming a device.
/// </para>
/// </summary>
public class RegisteredClientStaleErrorTests
{
    private static ApiError[] EveryStaleCase() =>
    [
        new DisableDeviceError.DeviceStale("plc-station-4", 3, 4),
        new DisableKioskError.KioskStale("plc-station-4", 3, 4),
        new RotateWebhookClientError.WebhookClientStale("plc-station-4", 3, 4),
    ];

    [Fact]
    public void Every_mutating_command_has_a_stale_case()
    {
        EveryStaleCase().Length.ShouldBe(3);
    }

    [Fact]
    public void Every_stale_case_carries_a_distinct_code_naming_its_own_client_kind()
    {
        string[] codes = [.. EveryStaleCase().Select(error => error.Code)];

        codes.ShouldBe(["DEVICE_STALE", "KIOSK_STALE", "WEBHOOK_CLIENT_STALE"]);
        codes.Distinct().Count().ShouldBe(codes.Length);
    }

    // 409 rather than 412: the caller can act on it, and it matches the
    // Conflict cases already in these unions (ADR-0113).
    [Fact]
    public void Every_stale_case_maps_to_409_conflict()
    {
        EveryStaleCase().ShouldAllBe(error => error.Status == HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_message_names_the_client_and_both_versions()
    {
        foreach (ApiError error in EveryStaleCase())
        {
            error.Message.ShouldContain("plc-station-4");
            error.Message.ShouldContain("3");
            error.Message.ShouldContain("4");
        }
    }

    [Fact]
    public void The_message_tells_the_caller_to_re_read_rather_than_retry()
    {
        EveryStaleCase().ShouldAllBe(error => error.Message.Contains("Re-read", StringComparison.Ordinal));
        EveryStaleCase().ShouldAllBe(error => !error.Message.Contains("Try again", StringComparison.OrdinalIgnoreCase));
    }
}
