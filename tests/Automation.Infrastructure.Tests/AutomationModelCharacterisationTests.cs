using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.Automation.Infrastructure.Persistence;

namespace SmartSentinelEye.Automation.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>RuleConfiguration.Configure</c> is split into private
/// methods. The refactor must leave the relational model exactly as it was: any
/// column, type, nullability, key, foreign key or index that moves is a schema
/// change the existing migrations would not describe, and nothing short of
/// Integration.Tests (CI, real Postgres) would otherwise notice.
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
/// A column added, removed or renamed, a type or nullability changed, a key
/// altered or an index dropped changes a line below and fails
/// <see cref="The_automation_model_has_exactly_this_shape"/>.
/// </para>
/// </summary>
public class AutomationModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "Creation -> table rules (schema default), owned=True, key (RuleId)",
        "  RuleId [RuleId]: uuid; RuleIdentifier; not null; converted to Guid; shadow",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (RuleId) -> rules (rule_id), required, ownership, on delete Cascade",
        "Rule -> table rules (schema default), owned=False, key (rule_id)",
        "  action_packed [Action]: text; RuleAction; not null; converted to String",
        "  archived_at [ArchivedAt]: timestamp with time zone; ArchivedAt; null; converted to DateTimeOffset",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  name [Name]: character varying(63); RuleName; not null; max 63; converted to String",
        "  predicate [Predicate]: character varying(4096); RulePredicate; not null; max 4096; converted to String",
        "  published_at [PublishedAt]: timestamp with time zone; PublishedAt; null; converted to DateTimeOffset",
        "  rule_id [Id]: uuid; RuleIdentifier; not null; converted to Guid",
        "  state [State]: character varying(16); RuleState; not null; max 16; converted to String",
        "  trigger_kind [TriggerKind]: character varying(128); TriggerKind; not null; max 128; converted to String",
        "  trigger_source [TriggerSource]: character varying(16); TriggerSource; not null; max 16; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_rules_fab_trigger_state (fab, trigger_source, trigger_kind, state)",
        "  index ux_rules_fab_name_active (fab, name) unique where state <> 'Archived'",
        "  navigation Creation -> Creation (reference), required dependent",
    ];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AutomationDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using AutomationDbContext context = new(options);
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

    internal static string[] RenderModel() =>
        [.. BuildModel().GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)];

    [Fact]
    public void The_automation_model_has_exactly_this_shape()
    {
        var actual = RenderModel();

        actual.ShouldBe(ExpectedModel);
    }
}
