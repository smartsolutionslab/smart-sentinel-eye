using System.Security.Claims;
using System.Text.Json;
using SmartSentinelEye.CameraCatalog.Api.Requests;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Characterisation (spec 408 / issue #3031 item 1, ADR-0144), captured green on <c>a73df3f1</c> before
/// <c>POST /cameras</c> learns to refuse a control character or whitespace anywhere in the address. An ordinary,
/// zoned, percent-encoded or Unicode format-character address is still registered (201), and an address an older
/// rule refuses is still a 400 whose detail is that rule's text, because the new rule is the last one. None of
/// these rows may be edited by the change.
/// </summary>
public class CameraUrlControlCharacterEndpointCharacterisationTests
{
    private const string Register = "/cameras/";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://a b/x", "a b", UserInfoRefusal },
        { "rtsp://[fe80::1] /x", "fe80", BracketTailRefusal },
        { "rtsp://admin:4242#@cam/s ", "4242", AtSignRefusal },
        { "rtsp://[fe80::1%25 ?evil]/s", "evil", DelimiterRefusal },
    };

    [Theory]
    [InlineData("rtsp://10.0.5.12/h264")]
    [InlineData("rtsp://[fe80::1%25eth0]:8554/x")]
    [InlineData("rtsp://cam/x%0D%0Ay%20z")]
    [InlineData("rtsp://cam/li\x200Bve")]
    public async Task An_ordinary_zoned_percent_encoded_or_format_character_address_is_still_registered(string address)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await RegisterAt(host, address);

        response.Status.ShouldBe(201, response.Body);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public async Task An_address_an_older_rule_refuses_is_still_a_400_with_that_rule_s_text_and_not_the_address(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await RegisterAt(host, address);

        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(refusal);
        response.Body.ShouldNotContain(secret, Case.Insensitive);
    }

    private static async Task<InProcessResponse> RegisterAt(CameraCatalogEndpointHost host, string address)
    {
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        try
        {
            return await host.SendAsync("POST", Register, CameraCatalogEndpointHost.Operator(Subject, "dresden"), new InProcessRequest(BodyJson: body));
        }
        catch (Exception escaped)
        {
            return new InProcessResponse(500, "unhandled " + escaped.GetType().Name, string.Empty);
        }
    }
}
