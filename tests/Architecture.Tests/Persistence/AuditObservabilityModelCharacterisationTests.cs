using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before
/// <c>AuditEventConfiguration.Configure</c> is split into private methods. The refactor must
/// leave the relational model exactly as it was: any column, type, nullability, length,
/// converter, key or index that moves is a schema change the existing migrations would not
/// describe, and nothing short of Integration.Tests (CI, real TimescaleDB) would otherwise
/// notice.
///
/// <para>
/// Counterfactuals this file was seen to catch: dropping <c>.IsUnique()</c> from
/// <c>ux_audit_event_identifier</c> changes that index line; raising <c>fab_attribution</c>'s
/// <c>HasMaxLength(16)</c> changes its column line; removing <c>IsRequired()</c> from the
/// <c>Payload</c> navigation changes the navigation line.
/// </para>
///
/// <para>
/// <b>Why here.</b> The sibling contexts keep this in their own <c>*.Infrastructure.Tests</c>
/// project. This context has none, and a new test project is out of proportion for one class;
/// <c>Architecture.Tests</c> already references <c>AuditObservability.Infrastructure</c>
/// (spec 352 J3).
/// </para>
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The debug string
/// embeds EF Core and Npgsql internals that shift on a patch bump. This reads the same facts
/// through the public metadata API and states them as the hand-written table in
/// <see cref="ExpectedModel"/>, cross-checked line by line against the committed
/// <c>AuditObservabilityDbContextModelSnapshot</c>.
/// <see cref="The_committed_model_snapshot_still_describes_the_model"/> asks EF itself whether
/// the snapshot and the model differ, so the check does not rest on this test's own output.
/// </para>
/// </summary>
public class AuditObservabilityModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "AuditEvent -> table audit_events (schema default), owned=False, key (audit_id, occurred_at)",
        "  actor_identifier [Actor]: uuid; ActorIdentifier; not null; column not null; converted to Guid",
        "  actor_username [ActorUsername]: character varying(255); ActorUsername; null; column null; max 255; converted to String",
        "  audit_id [Id]: uuid; AuditEventIdentifier; not null; column not null; converted to Guid",
        "  event_identifier [EventIdentifier]: uuid; EventIdentifier; not null; column not null; converted to Guid",
        "  event_kind [EventKind]: character varying(100); EventKind; not null; column not null; max 100; converted to String",
        "  fab_attribution [FabAttribution]: character varying(16); FabAttribution; not null; column not null; max 16; converted to String",
        "  fab_id [Fab]: character varying(32); FabIdentifier; null; column null; max 32; converted to String",
        "  handler_entered_at [HandlerEnteredAt]: timestamp with time zone; HandlerEnteredAt; null; column null; converted to DateTimeOffset",
        "  occurred_at [OccurredAt]: timestamp with time zone; OccurredAt; not null; column not null; converted to DateTimeOffset",
        "  received_at [ReceivedAt]: timestamp with time zone; ReceivedAt; not null; column not null; converted to DateTimeOffset",
        "  resource_identifier [ResourceIdentifier]: character varying(255); ResourceIdentifier; null; column null; max 255; converted to String",
        "  resource_kind [ResourceKind]: character varying(32); ResourceKind; null; column null; max 32; converted to String",
        "  schema_version [SchemaVersion]: smallint; SchemaVersion; not null; column not null; converted to Int16",
        "  written_at [WrittenAt]: timestamp with time zone; WrittenAt; null; column null; converted to DateTimeOffset",
        "  index ix_audit_actor_occurred (actor_identifier, occurred_at)",
        "  index ix_audit_fab_occurred (fab_id, occurred_at)",
        "  index ix_audit_kind_occurred (event_kind, occurred_at)",
        "  index ix_audit_resource_occurred (resource_kind, resource_identifier, occurred_at)",
        "  index ux_audit_event_identifier (event_identifier, occurred_at) unique",
        "  navigation Payload -> StoredPayload (reference), required dependent",
        "StoredPayload -> table audit_events (schema default), owned=True, key (audit_id, occurred_at)",
        "  audit_id [AuditEventId]: uuid; AuditEventIdentifier; not null; column not null; converted to Guid; shadow",
        "  occurred_at [AuditEventOccurredAt]: timestamp with time zone; OccurredAt; not null; column not null; converted to DateTimeOffset; shadow",
        "  payload [Content]: jsonb; AuditPayload; not null; column not null; converted to String",
        "  payload_size_bytes [Size]: integer; PayloadSizeBytes; not null; column not null; converted to Int32",
        "  fk (audit_id, occurred_at) -> audit_events (audit_id, occurred_at), required, ownership, on delete Cascade",
    ];



    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using AuditObservabilityDbContext context = new(options);
        return ((IInfrastructure<IServiceProvider>)context).Instance
            .GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");
    }

    private static string Column(IProperty property)
    {
        var table = property.DeclaringType.GetTableName();
        return table is null
            ? property.Name
            : property.GetColumnName(StoreObjectIdentifier.Table(table, property.DeclaringType.GetSchema())) ?? property.Name;
    }

    private static bool IsColumnNullable(IProperty property)
    {
        var table = property.DeclaringType.GetTableName();
        return table is not null
            && property.IsColumnNullable(StoreObjectIdentifier.Table(table, property.DeclaringType.GetSchema()));
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
            IsColumnNullable(property) ? "column null" : "column not null",
        };

        if (property.GetMaxLength() is { } maxLength)
        {
            details.Add($"max {maxLength}");
        }

        var providerType = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType();
        if (providerType is not null)
        {
            details.Add($"converted to {TypeName(providerType)}");
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

        return $"  {Column(property)} [{property.Name}]: {string.Join("; ", details)}";
    }

    private static string Describe(IForeignKey foreignKey) =>
        $"  fk ({Join(foreignKey.Properties.Select(property => Column(property)))}) -> "
        + $"{foreignKey.PrincipalEntityType.GetTableName()} "
        + $"({Join(foreignKey.PrincipalKey.Properties.Select(property => Column(property)))}), "
        + $"{(foreignKey.IsRequired ? "required" : "optional")}, "
        + $"{(foreignKey.IsOwnership ? "ownership" : "reference")}, "
        + $"on delete {foreignKey.DeleteBehavior}";

    private static string Describe(IIndex index) =>
        $"  index {index.GetDatabaseName()} ({Join(index.Properties.Select(property => Column(property)))})"
        + (index.IsUnique ? " unique" : string.Empty)
        + (index.GetFilter() is { } filter ? $" where {filter}" : string.Empty);

    private static IEnumerable<string> Describe(IEntityType entityType)
    {
        yield return $"{entityType.DisplayName()} -> table {entityType.GetTableName()}"
            + $" (schema {entityType.GetSchema() ?? "default"}), owned={entityType.IsOwned()},"
            + $" key ({Join(entityType.FindPrimaryKey()!.Properties.Select(property => Column(property)))})";

        foreach (var property in entityType.GetDeclaredProperties().OrderBy(property => Column(property), StringComparer.Ordinal))
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

    [Fact]
    public void The_audit_observability_model_has_exactly_this_shape()
    {
        var model = BuildModel();

        var actual = model.GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)
            .ToArray();

        actual.ShouldBe(ExpectedModel);
    }

    [Fact]
    public void The_committed_model_snapshot_still_describes_the_model()
    {
        var options = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using AuditObservabilityDbContext context = new(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
