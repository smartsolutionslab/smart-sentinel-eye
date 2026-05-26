namespace SmartSentinelEye.Integration.Tests.Fixtures;

[CollectionDefinition(Name)]
#pragma warning disable CA1711 // xUnit convention requires the "Collection" suffix
public class AspireCollection : ICollectionFixture<AspireFixture>
#pragma warning restore CA1711
{
    public const string Name = "Aspire";
}
