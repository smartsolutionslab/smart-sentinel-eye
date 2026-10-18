using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Migrations;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Spec 403 (#3008), adversarial pass on what crosses the broker and what is stored.
///
/// <para>
/// The wire facts use the serializer Wolverine 6.48.3 actually installs: <see cref="WolverineOptions"/> wires
/// <see cref="SystemTextJsonSerializer"/> with its <c>DefaultOptions()</c> (camelCase names, case-insensitive
/// reads, strict number handling) in its constructor, and no StreamDistribution or CameraCatalog source
/// replaces it. A value the deserializer cannot bind throws, and Wolverine's
/// <c>HandlerPipeline.TryDeserializeEnvelope</c> answers any exception with <c>MoveToErrorQueue</c>, so a
/// malformed <c>previousVersion</c> fails the message; it is never applied as a legacy, version-less change.
/// </para>
/// </summary>
public class CameraAddressChangedWireAdversarialTests
{
    private const string Body =
        """{"camera":"0197a1b2-0000-7000-8000-0000000000f4","fab":"munich","previousUrl":"rtsp://camera-sim:8554/a","url":"rtsp://camera-sim:8554/b","changedAt":"2026-10-14T09:00:00+00:00","changedBy":"0197a1b2-0000-7000-8000-000000000001","metadata":{"eventIdentifier":"00000000-0000-0000-0000-0000000000ac","occurredAt":"2026-10-14T09:00:00+00:00","fab":"munich","actor":null}__VERSION__}""";

    private static IMessageSerializer WolverineDefault() => new WolverineOptions().DefaultSerializer;

    private static CameraAddressChangedV1 Read(string versionMember)
    {
        var json = Body.Replace("__VERSION__", versionMember, StringComparison.Ordinal);
        Envelope envelope = new() { Data = Encoding.UTF8.GetBytes(json) };

        return (CameraAddressChangedV1)WolverineDefault().ReadFromData(typeof(CameraAddressChangedV1), envelope);
    }

    [Fact]
    public void Wolverines_default_serializer_is_system_text_json()
    {
        WolverineDefault().ShouldBeOfType<SystemTextJsonSerializer>();
    }

    [Fact]
    public void The_publisher_writes_the_member_in_camel_case_and_the_consumer_reads_it_back()
    {
        var serializer = WolverineDefault();
        var sent = Read(",\"previousVersion\":12");

        var bytes = serializer.WriteMessage(sent);
        Envelope envelope = new() { Data = bytes };
        var received = (CameraAddressChangedV1)serializer.ReadFromData(typeof(CameraAddressChangedV1), envelope);

        Encoding.UTF8.GetString(bytes).ShouldContain("\"previousVersion\":12");
        received.PreviousVersion.ShouldBe(12);
        received.ShouldBe(sent);
    }

    [Theory]
    [InlineData(",\"previousVersion\":7", 7)]
    [InlineData(",\"PreviousVersion\":7", 7)]
    [InlineData(",\"PREVIOUSVERSION\":7", 7)]
    [InlineData(",\"previousVersion\":0", 0)]
    [InlineData(",\"previousVersion\":2147483647", int.MaxValue)]
    [InlineData(",\"previousVersion\":-1", -1)]
    public void A_well_formed_integer_binds_whatever_the_member_casing(string member, int expected)
    {
        Read(member).PreviousVersion.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"previousVersion\":null")]
    public void A_missing_or_null_member_reads_as_a_version_less_legacy_change(string member)
    {
        var message = Read(member);

        message.PreviousVersion.ShouldBeNull();
        message.Url.ShouldBe("rtsp://camera-sim:8554/b");
    }

    [Theory]
    [InlineData(",\"previousVersion\":\"7\"")]
    [InlineData(",\"previousVersion\":7.5")]
    [InlineData(",\"previousVersion\":7.0")]
    [InlineData(",\"previousVersion\":7e0")]
    [InlineData(",\"previousVersion\":2147483648")]
    [InlineData(",\"previousVersion\":-2147483649")]
    [InlineData(",\"previousVersion\":true")]
    [InlineData(",\"previousVersion\":[]")]
    [InlineData(",\"previousVersion\":{}")]
    public void A_malformed_member_fails_the_message_rather_than_reading_as_legacy(string member)
    {
        Should.Throw<JsonException>(() => Read(member));
    }

    /// <summary>
    /// Duplicate members (one per casing) are not refused: the last one read wins. Recorded because it is how
    /// a crafted body can show one value to a reader that matches case-sensitively and another to Wolverine.
    /// </summary>
    [Fact]
    public void Duplicate_members_in_different_casings_bind_the_last_one()
    {
        Read(",\"previousVersion\":3,\"PreviousVersion\":9").PreviousVersion.ShouldBe(9);
    }

    [Fact]
    public void The_migration_adds_exactly_one_nullable_integer_column_with_no_default_and_its_down_drops_only_that()
    {
        RecordStreamSourceCameraVersion migration = new();

        var added = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<AddColumnOperation>();
        added.Table.ShouldBe("streams");
        added.Name.ShouldBe("source_camera_version");
        added.ClrType.ShouldBe(typeof(int));
        added.ColumnType.ShouldBe("integer");
        added.IsNullable.ShouldBeTrue();
        added.DefaultValue.ShouldBeNull();
        added.DefaultValueSql.ShouldBeNull();
        added.ComputedColumnSql.ShouldBeNull();

        var dropped = migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropColumnOperation>();
        dropped.Table.ShouldBe("streams");
        dropped.Name.ShouldBe("source_camera_version");
    }

    [Fact]
    public void The_snapshot_gained_exactly_one_property_on_streams_and_it_is_nullable()
    {
        StreamDistributionDbContextModelSnapshot snapshot = new();
        var version = snapshot.Model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() == "streams")
            .SelectMany(entityType => entityType.GetDeclaredProperties())
            .Where(property => property.GetColumnName() == "source_camera_version")
            .ShouldHaveSingleItem();
        version.IsNullable.ShouldBeTrue();
        version.ClrType.ShouldBe(typeof(int?));
        version.GetColumnName().ShouldBe("source_camera_version");
        version.GetDefaultValue().ShouldBeNull();
        version.GetDefaultValueSql().ShouldBeNull();
    }

    /// <summary>
    /// A stored negative value (only SQL tampering can write one) fails materialisation loudly through the
    /// value object's guard rather than loading as an ordering key.
    /// </summary>
    [Fact]
    public void A_stored_negative_version_is_refused_when_read_back()
    {
        var property = BuildModel()
            .GetEntityTypes()
            .Where(entityType => entityType.GetTableName() == "streams")
            .SelectMany(entityType => entityType.GetDeclaredProperties())
            .Where(candidate => candidate.GetColumnName() == "source_camera_version")
            .ShouldHaveSingleItem();
        var converter = property.GetValueConverter().ShouldNotBeNull();

        Should.Throw<ArgumentException>(() => converter.ConvertFromProvider(-1));
        converter.ConvertFromProvider(int.MaxValue)!.ToString().ShouldBe("2147483647");
    }

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<StreamDistributionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using StreamDistributionDbContext context = new(options);
        return ((IInfrastructure<IServiceProvider>)context).Instance
            .GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");
    }
}
