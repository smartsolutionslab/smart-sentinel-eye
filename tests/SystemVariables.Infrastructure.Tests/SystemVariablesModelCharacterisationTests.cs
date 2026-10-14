using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>VariableConfiguration.Configure</c> is split into
/// private methods. The refactor must leave the relational model exactly as it
/// was: a column, store type, length, converter, nullability, key, index or
/// filter that moves is a schema change the existing migrations would not
/// describe, and nothing short of Integration.Tests (CI, real Postgres) would
/// otherwise notice.
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The
/// debug string embeds EF Core and Npgsql internals that shift on a patch bump;
/// a snapshot of it would be a standing source of false failures. This reads the
/// same facts through the public metadata API and states them as the table in
/// <see cref="ExpectedModel"/>, which is written by hand and reviewed against
/// the configuration and the applied migrations, not produced by the code under
/// test.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: dropping the <c>HasFilter</c> from the fab/name
/// index turns <see cref="The_system_variables_model_has_exactly_this_shape"/>
/// red.
/// </para>
/// </summary>
public class SystemVariablesModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "BooleanLabels -> table system_variables (schema default), owned=True, key (VariableId)",
        "  VariableId [VariableId]: uuid; VariableIdentifier; not null; converted to Guid; shadow",
        "  falsy_label [FalsyLabel]: character varying(64); String; not null; max 64",
        "  truthy_label [TruthyLabel]: character varying(64); String; not null; max 64",
        "  fk (VariableId) -> system_variables (variable_id), required, ownership, on delete Cascade",
        "Creation -> table system_variables (schema default), owned=True, key (VariableId)",
        "  VariableId [VariableId]: uuid; VariableIdentifier; not null; converted to Guid; shadow",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (VariableId) -> system_variables (variable_id), required, ownership, on delete Cascade",
        "Variable -> table system_variables (schema default), owned=False, key (variable_id)",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  name [Name]: character varying(64); VariableName; not null; max 64; converted to String",
        "  state [State]: character varying(16); VariableState; not null; max 16; converted to String",
        "  type [Type]: character varying(16); VariableType; not null; max 16; converted to String",
        "  value_packed [Value]: character varying(512); VariableValue; not null; max 512; converted to String",
        "  variable_id [Id]: uuid; VariableIdentifier; not null; converted to Guid",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ux_system_variables_fab_name_active (fab, name) unique where state <> 'Archived'",
        "  navigation BooleanLabels -> BooleanLabels (reference)",
        "  navigation Creation -> Creation (reference), required dependent",
    ];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SystemVariablesDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using SystemVariablesDbContext context = new(options);
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
    public void The_system_variables_model_has_exactly_this_shape()
    {
        var actual = RenderModel();

        actual.ShouldBe(ExpectedModel);
    }
}
