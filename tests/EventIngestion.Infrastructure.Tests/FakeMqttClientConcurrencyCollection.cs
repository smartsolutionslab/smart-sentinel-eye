namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Keeps <see cref="FakeMqttClientConcurrencyTests"/> off the thread pool while
/// anything else in this assembly is running. xUnit parallelises test classes
/// by default, and each of that class's two facts spins a synchronous reader
/// flat-out for its whole run (up to <c>OverallBound</c>, 30s) while a writer
/// does 20,000 iterations alongside it. <c>MqttConnectionLoopTests</c>, run
/// concurrently by default, asserts wall-clock windows as tight as 110-150ms;
/// under that CPU pressure on a loaded or narrow-core runner those assertions
/// can miss for reasons that have nothing to do with the backoff logic they
/// cover.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
#pragma warning disable CA1711 // xUnit convention requires the "Collection" suffix
public class FakeMqttClientConcurrencyCollection
#pragma warning restore CA1711
{
    public const string Name = "fake-mqtt-client-concurrency";

    // xUnit reads the attribute off this type; nothing constructs it.
    protected FakeMqttClientConcurrencyCollection()
    {
    }
}
