using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Migrations;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on unmodified
/// <c>develop</c> (<c>8ded100e</c>) before <c>StreamConfiguration.Configure</c>
/// is split into private methods. The refactor must leave the relational model
/// exactly as it was: any column, type, nullability, key or index that moves is
/// a schema change the existing migrations would not describe, and nothing short
/// of Integration.Tests (CI, real Postgres) would otherwise notice.
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The
/// debug string embeds EF Core and Npgsql internals that shift on a patch bump,
/// which Dependabot merges weekly; a snapshot of it would be a standing source
/// of false failures. This reads the same facts through the public metadata API
/// and states them as the table in <see cref="ExpectedModel"/>: per entity its
/// table and primary key, per property its column, store type, CLR type,
/// nullability, length, converter, default and concurrency flag, then its
/// foreign keys, indexes and navigations.
/// </para>
///
/// <para>
/// <b>Cross-checked against the committed snapshot.</b> The literal was not
/// taken on trust from the code under refactor: the second test reads the same
/// columns (name, store type, nullability, length, concurrency) and indexes out of
/// <c>StreamDistributionDbContextModelSnapshot</c>, which migrations were
/// generated from, and requires the live model to agree. The owned
/// <c>Provisioning</c> columns are the case the snapshot exists to guard: without
/// <c>Navigation(...).IsRequired()</c> the model makes them nullable while the
/// database keeps them NOT NULL (issue #2022).
/// </para>
///
/// <para>
/// Counterfactual this file was seen to catch: changing <c>HasMaxLength(80)</c>
/// on <c>mediamtx_path</c> fails both tests; removing
/// <c>builder.Navigation(...).IsRequired()</c> fails both.
/// </para>
/// </summary>
public class StreamDistributionModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "Provisioning -> table streams (schema default), owned=True, key (StreamId)",
        "  StreamId [StreamId]: uuid; StreamIdentifier; not null; converted to Guid; shadow",
        "  provisioned_at [At]: timestamp with time zone; ProvisionedAt; not null; converted to DateTimeOffset",
        "  provisioned_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (StreamId) -> streams (stream_id), required, ownership, on delete Cascade",
        "Stream -> table streams (schema default), owned=False, key (stream_id)",
        "  camera_id [Camera]: uuid; CameraIdentifier; not null; converted to Guid",
        "  fab [Fab]: character varying(32); FabIdentifier; null; max 32; converted to String",
        "  last_error [LastError]: character varying(1024); StreamError; null; max 1024; converted to String",
        "  last_success_at [LastSuccessAt]: timestamp with time zone; LastSuccessAt; null; converted to DateTimeOffset",
        "  mediamtx_path [Path]: character varying(80); MediaMtxPath; not null; max 80; converted to String",
        "  source_url [SourceUrl]: character varying(2048); StreamSourceUrl; not null; max 2048; converted to String",
        "  state [State]: character varying(16); StreamState; not null; max 16; converted to String",
        "  stream_id [Id]: uuid; StreamIdentifier; not null; converted to Guid",
        "  transcode_mode [TranscodeMode]: character varying(16); TranscodeMode; not null; max 16; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_streams_fab (fab)",
        "  index ux_streams_camera_id (camera_id) unique",
        "  index ux_streams_mediamtx_path (mediamtx_path) unique",
        "  navigation Provisioning -> Provisioning (reference), required dependent",
    ];

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

    private static string Join(IEnumerable<string> names) => string.Join(", ", names);

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{TypeName(underlying)}?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var arguments = string.Join(", ", type.GetGenericArguments().Select(TypeName));
        return $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{arguments}>";
    }

    private static bool HasExplicitDefault(IProperty property) =>
        property.GetDefaultValue() is { } defaultValue
        && !defaultValue.Equals(Activator.CreateInstance(property.ClrType.IsValueType ? property.ClrType : typeof(object)));

    private static string Describe(IProperty property)
    {
        var details = new List<string>
        {
            property.GetColumnType(),
            TypeName(property.ClrType),
            property.IsNullable ? "null" : "not null",
        };

        if (property.GetMaxLength() is { } maxLength)
        {
            details.Add($"max {maxLength}");
        }

        if (property.GetValueConverter() is { } converter)
        {
            details.Add($"converted to {TypeName(converter.ProviderClrType)}");
        }

        if (HasExplicitDefault(property))
        {
            details.Add($"default {property.GetDefaultValue()}");
        }

        if (property.ValueGenerated != ValueGenerated.Never)
        {
            details.Add($"generated {property.ValueGenerated}");
        }

        if (property.IsConcurrencyToken)
        {
            details.Add("concurrency token");
        }

        if (property.IsShadowProperty())
        {
            details.Add("shadow");
        }

        return $"  {property.GetColumnName()} [{property.Name}]: {string.Join("; ", details)}";
    }

    private static string Describe(IForeignKey foreignKey) =>
        $"  fk ({Join(foreignKey.Properties.Select(property => property.GetColumnName()))}) -> "
        + $"{foreignKey.PrincipalEntityType.GetTableName()} "
        + $"({Join(foreignKey.PrincipalKey.Properties.Select(property => property.GetColumnName()))}), "
        + $"{(foreignKey.IsRequired ? "required" : "optional")}, "
        + $"{(foreignKey.IsOwnership ? "ownership" : "reference")}, "
        + $"on delete {foreignKey.DeleteBehavior}";

    private static string Describe(IIndex index) =>
        $"  index {index.GetDatabaseName()} ({Join(index.Properties.Select(property => property.GetColumnName()))})"
        + (index.IsUnique ? " unique" : string.Empty)
        + (index.GetFilter() is { } filter ? $" where {filter}" : string.Empty);

    private static IEnumerable<string> Describe(IEntityType entityType)
    {
        yield return $"{entityType.DisplayName()} -> table {entityType.GetTableName()}"
            + $" (schema {entityType.GetSchema() ?? "default"}), owned={entityType.IsOwned()},"
            + $" key ({Join(entityType.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()))})";

        foreach (var property in entityType.GetDeclaredProperties().OrderBy(property => property.GetColumnName(), StringComparer.Ordinal))
        {
            yield return Describe(property);
        }

        foreach (var foreignKey in entityType.GetDeclaredForeignKeys().OrderBy(key => key.PrincipalEntityType.Name, StringComparer.Ordinal))
        {
            yield return Describe(foreignKey);
        }

        foreach (var index in entityType.GetDeclaredIndexes().OrderBy(index => index.GetDatabaseName(), StringComparer.Ordinal))
        {
            yield return Describe(index);
        }

        foreach (var navigation in entityType.GetDeclaredNavigations().OrderBy(navigation => navigation.Name, StringComparer.Ordinal))
        {
            yield return $"  navigation {navigation.Name} -> {navigation.TargetEntityType.DisplayName()}"
                + (navigation.IsCollection ? " (collection)" : " (reference)")
                + (navigation.ForeignKey.IsRequiredDependent && !navigation.IsOnDependent ? ", required dependent" : string.Empty);
        }
    }

    private static string[] RenderModel() =>
        [.. BuildModel().GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)];

    [Fact]
    public void The_stream_distribution_model_has_exactly_this_shape()
    {
        var actual = RenderModel();

        actual.ShouldBe(ExpectedModel);
    }

    [Fact]
    public void The_streams_table_has_the_columns_and_indexes_the_committed_snapshot_describes()
    {
        var live = BuildModel();
        StreamDistributionDbContextModelSnapshot snapshot = new();

        ColumnsOfStreamsTable(live).ShouldBe(ColumnsOfStreamsTable(snapshot.Model));
        IndexesOfStreamsTable(live).ShouldBe(IndexesOfStreamsTable(snapshot.Model));
    }

    private static string[] ColumnsOfStreamsTable(IReadOnlyModel model) =>
        [.. model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() == "streams")
            .SelectMany(entityType => entityType.GetDeclaredProperties())
            .Select(property =>
                $"{property.GetColumnName(StoreObjectIdentifier.Table("streams"))}: {property.GetColumnType()}; "
                + (property.IsNullable ? "null" : "not null")
                + $"; max {property.GetMaxLength()?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}"
                + (property.IsConcurrencyToken ? "; concurrency token" : string.Empty))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)];

    private static string[] IndexesOfStreamsTable(IReadOnlyModel model) =>
        [.. model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() == "streams")
            .SelectMany(entityType => entityType.GetDeclaredIndexes())
            .Select(index =>
                $"{index.GetDatabaseName()} ({Join(index.Properties.Select(property => property.GetColumnName()))})"
                + (index.IsUnique ? " unique" : string.Empty)
                + (index.GetFilter() is { } filter ? $" where {filter}" : string.Empty))
            .OrderBy(line => line, StringComparer.Ordinal)];
}
