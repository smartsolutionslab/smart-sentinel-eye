using System.Text;
using System.Text.Json;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class PayloadTests
{
    [Fact]
    public void From_string_round_trips_a_simple_object()
    {
        Payload payload = Payload.From("{\"cycleId\":\"abc\"}");
        payload.Value.ShouldBe("{\"cycleId\":\"abc\"}");
    }

    [Fact]
    public void From_string_canonicalises_redundant_whitespace()
    {
        Payload payload = Payload.From("{  \"a\" :  1   }");
        payload.Value.ShouldBe("{\"a\":1}");
    }

    [Fact]
    public void From_string_rejects_non_JSON()
    {
        Action act = () => Payload.From("not-json");
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_string_rejects_payload_above_64_KB()
    {
        string overlong = "{\"data\":\"" + new string('a', Payload.MaximumBytes) + "\"}";
        Encoding.UTF8.GetByteCount(overlong).ShouldBeGreaterThan(Payload.MaximumBytes);
        Action act = () => Payload.From(overlong);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_document_round_trips()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"a\":1,\"b\":[true,false]}");
        Payload.From(doc).Value.ShouldBe("{\"a\":1,\"b\":[true,false]}");
    }

    [Fact]
    public void From_document_rejects_above_limit()
    {
        string overlong = "{\"data\":\"" + new string('a', Payload.MaximumBytes) + "\"}";
        using JsonDocument doc = JsonDocument.Parse(overlong);
        Action act = () => Payload.From(doc);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_element_rejects_an_undefined_element()
    {
        JsonElement undefined = default;
        undefined.ValueKind.ShouldBe(JsonValueKind.Undefined);

        Action act = () => Payload.From(undefined);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("missing", Case.Insensitive);
    }

    [Fact]
    public void From_element_accepts_a_json_null_element()
    {
        using JsonDocument doc = JsonDocument.Parse("null");
        Payload.From(doc.RootElement).Value.ShouldBe("null");
    }

    [Fact]
    public void From_element_accepts_a_scalar_element()
    {
        using JsonDocument doc = JsonDocument.Parse("\"oops\"");
        Payload.From(doc.RootElement).Value.ShouldBe("\"oops\"");
    }

    [Fact]
    public void From_element_round_trips_an_object()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"cycleId\":\"abc\"}");
        Payload.From(doc.RootElement).Value.ShouldBe("{\"cycleId\":\"abc\"}");
    }
}
