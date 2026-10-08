using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

/// <summary>
/// T002 (spec 317, #2325) — plan.md §6.3. One factory for the envelope-level
/// topic grammar (<c>event/{fab}/{source}/{device}</c>), shared by both ingest
/// handlers and the persistence loop's refusal path rather than composed with
/// an interpolated string in three places.
/// </summary>
public class DeliveryTopicForEnvelopeTests
{
    [Fact]
    public void ForEnvelope_composes_the_event_topic_grammar()
    {
        DeliveryTopic topic = DeliveryTopic.ForEnvelope(
            FabIdentifier.From("berlin"), Source.Manual, DeviceIdentifier.From("station-4"));

        topic.Value.ShouldBe("event/berlin/manual/station-4");
    }

    [Fact]
    public void ForEnvelope_varies_with_each_segment()
    {
        DeliveryTopic topic = DeliveryTopic.ForEnvelope(
            FabIdentifier.From("dresden"), Source.Inference, DeviceIdentifier.From("cam-7"));

        topic.Value.ShouldBe("event/dresden/inference/cam-7");
    }
}
