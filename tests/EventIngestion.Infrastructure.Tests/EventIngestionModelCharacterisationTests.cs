using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 346, ADR-0144), captured green on unmodified
/// <c>develop</c> before the five EF configurations of this context are split
/// into private methods. The refactor must leave the relational model exactly
/// as it was: any column, type, nullability, key, foreign key or index that
/// moves is a schema change the existing migrations would not describe, and
/// nothing short of Integration.Tests (CI, real Postgres) would otherwise
/// notice.
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The
/// debug string embeds EF Core and Npgsql internals that shift on a patch bump,
/// which Dependabot merges weekly; a snapshot of it would be a standing source
/// of false failures. This reads the same facts through the public metadata
/// API and states them as the table in <see cref="ExpectedModel"/>: per entity
/// its table and primary key, per property its column, store type, CLR type,
/// nullability, length, converter, default and concurrency flag, then its
/// foreign keys, indexes and navigations.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> What the database does with the model — the
/// partitioned <c>events</c> table, the check constraints and the real
/// round-trip — is only observable against Postgres and stays with
/// Integration.Tests in CI. This pins the model EF would hand to a migration.
/// </para>
/// </summary>
public class EventIngestionModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "DeadLetter -> table dead_letters (schema default), owned=False, key (dead_letter_id)",
        "  dead_letter_id [Id]: uuid; DeadLetterIdentifier; not null; converted to Guid",
        "  error [Error]: character varying(512); RejectionReason; not null; max 512; converted to String",
        "  fab [Fab]: character varying(32); FabIdentifier; null; max 32; converted to String",
        "  kind [Kind]: character varying(128); Kind; null; max 128; converted to String",
        "  raw_payload [RawPayload]: text; RawPayload; not null; converted to String",
        "  reason [Reason]: text; DeadLetterReason; not null; converted to String",
        "  rejected_at [RejectedAt]: timestamp with time zone; RejectedAt; not null; converted to DateTimeOffset",
        "  state [State]: text; HoldState; not null; converted to String; default Held; generated OnAdd",
        "  topic [Topic]: character varying(256); DeliveryTopic; not null; max 256; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_dead_letters_fab (fab)",
        "  index ix_dead_letters_fab_reason_state (fab, reason, state)",
        "  index ix_dead_letters_rejected_at (rejected_at)",
        "Declaration -> table source_modes (schema default), owned=True, key (SourceModeId)",
        "  SourceModeId [SourceModeId]: uuid; SourceModeIdentifier; not null; converted to Guid; shadow",
        "  declared_at [DeclaredAt]: timestamp with time zone; DeclaredAt; not null; converted to DateTimeOffset",
        "  declared_by [DeclaredBy]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (SourceModeId) -> source_modes (source_mode_id), required, ownership, on delete Cascade",
        "Event -> table events (schema default), owned=False, key (fab_id, event_id, ingested_at)",
        "  device_id [Device]: character varying(64); DeviceIdentifier; not null; max 64; converted to String",
        "  event_id [Id]: uuid; EventIdentifier; not null; converted to Guid",
        "  fab_id [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  ingested_at [IngestedAt]: timestamp with time zone; IngestedAt; not null; converted to DateTimeOffset",
        "  kind [Kind]: character varying(128); Kind; not null; max 128; converted to String",
        "  occurred_at [OccurredAt]: timestamp with time zone; OccurredAt; not null; converted to DateTimeOffset",
        "  payload [Payload]: jsonb; Payload; not null; converted to String",
        "  source [Source]: character varying(16); Source; not null; max 16; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "RegisteredEventType -> table registered_event_types (schema default), owned=False, key (registered_event_type_id)",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  kind [Kind]: character varying(128); Kind; not null; max 128; converted to String",
        "  registered_event_type_id [Id]: uuid; RegisteredEventTypeIdentifier; not null; converted to Guid",
        "  state [State]: character varying(16); RegistrationState; not null; max 16; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_registered_event_types_fab (fab)",
        "  index ux_registered_event_types_fab_kind (fab, kind) unique where state <> 'Retired'",
        "  navigation Registration -> Registration (reference), required dependent",
        "Registration -> table registered_event_types (schema default), owned=True, key (RegisteredEventTypeId)",
        "  RegisteredEventTypeId [RegisteredEventTypeId]: uuid; RegisteredEventTypeIdentifier; not null; converted to Guid; shadow",
        "  registered_at [RegisteredAt]: timestamp with time zone; RegisteredAt; not null; converted to DateTimeOffset",
        "  registered_by [RegisteredBy]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (RegisteredEventTypeId) -> registered_event_types (registered_event_type_id), required, ownership, on delete Cascade",
        "SourceMode -> table source_modes (schema default), owned=False, key (source_mode_id)",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  mode [Mode]: character varying(16); EventTypeMode; not null; max 16; converted to String",
        "  source [Source]: character varying(16); Source; not null; max 16; converted to String",
        "  source_mode_id [Id]: uuid; SourceModeIdentifier; not null; converted to Guid",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ux_source_modes_fab_source (fab, source) unique",
        "  navigation Declaration -> Declaration (reference), required dependent",
        "WebhookIntegration -> table webhook_integrations (schema default), owned=False, key (integration_id)",
        "  default_kind [DefaultKind]: character varying(128); Kind; not null; max 128; converted to String",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  integration_id [Id]: uuid; WebhookIntegrationIdentifier; not null; converted to Guid",
        "  keycloak_client_id [KeycloakClientId]: character varying(255); KeycloakClientIdentifier; null; max 255; converted to String",
        "  name [Name]: character varying(63); WebhookIntegrationName; not null; max 63; converted to String",
        "  registered_at [RegisteredAt]: timestamp with time zone; RegisteredAt; not null; converted to DateTimeOffset",
        "  revoked_at [RevokedAt]: timestamp with time zone; RevokedAt; null; converted to DateTimeOffset",
        "  rotated_at [RotatedAt]: timestamp with time zone; RotatedAt; null; converted to DateTimeOffset",
        "  token_hash [TokenHash]: character varying(128); BearerTokenHash; not null; max 128; converted to String",
        "  validation_mode [ValidationMode]: integer; BearerValidationMode; not null; generated OnAdd",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_webhook_integrations_fab (fab)",
        "  index ux_webhook_integrations_name (name) unique",
    ];
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<EventIngestionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using EventIngestionDbContext context = new(options);
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

    [Fact]
    public void The_event_ingestion_model_has_exactly_this_shape()
    {
        var model = BuildModel();

        var actual = model.GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)
            .ToArray();

        actual.ShouldBe(ExpectedModel);
    }
}
