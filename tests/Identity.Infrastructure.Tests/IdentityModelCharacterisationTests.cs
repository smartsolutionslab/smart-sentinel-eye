using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.Identity.Infrastructure.Persistence;

namespace SmartSentinelEye.Identity.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on the unmodified
/// <c>RegisteredClientConfiguration</c> before its <c>Configure</c> is split
/// into private methods. The refactor must leave the relational model exactly
/// as it was: any column, type, nullability, key, foreign key or index that
/// moves is a schema change the existing migrations would not describe, and
/// nothing short of Integration.Tests (CI, real Postgres) would otherwise
/// notice.
///
/// <para>
/// Reads the logical shape through the public metadata API rather than
/// <c>Model.ToDebugString()</c>, whose EF Core and Npgsql internals shift on a
/// patch bump. The expected shape is the hand-written table
/// <see cref="ExpectedModel"/>.
/// </para>
///
/// <para>
/// Counterfactual this file was seen to catch: changing a column's max length,
/// renaming <c>ux_registered_clients_clientid_active</c>, or dropping the
/// <c>IsRequired()</c> on the owned <c>Registration</c> navigation each fails
/// <see cref="The_identity_model_has_exactly_this_shape"/>.
/// </para>
/// </summary>
public class IdentityModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "RegisteredClient -> table registered_clients (schema default), owned=False, key (registered_client_id)",
        "  client_id [ClientId]: character varying(255); ClientId; not null; max 255; converted to String",
        "  disabled_at [DisabledAt]: timestamp with time zone; DisabledAt; null; converted to DateTimeOffset",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  kind [Kind]: character varying(32); ClientKind; not null; max 32; converted to String",
        "  last_rotated_at [LastRotatedAt]: timestamp with time zone; LastRotatedAt; null; converted to DateTimeOffset",
        "  registered_client_id [Id]: uuid; RegisteredClientIdentifier; not null; converted to Guid",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_registered_clients_kind_fab_disabled (kind, fab, disabled_at)",
        "  index ux_registered_clients_clientid_active (client_id) unique where disabled_at IS NULL",
        "  navigation Registration -> Registration (reference), required dependent",
        "Registration -> table registered_clients (schema default), owned=True, key (RegisteredClientId)",
        "  RegisteredClientId [RegisteredClientId]: uuid; RegisteredClientIdentifier; not null; converted to Guid; shadow",
        "  registered_at [At]: timestamp with time zone; RegisteredAt; not null; converted to DateTimeOffset",
        "  registered_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (RegisteredClientId) -> registered_clients (registered_client_id), required, ownership, on delete Cascade",
    ];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using IdentityDbContext context = new(options);
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
    public void The_identity_model_has_exactly_this_shape()
    {
        var model = BuildModel();

        var actual = model.GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)
            .ToArray();

        actual.ShouldBe(ExpectedModel);
    }
}
