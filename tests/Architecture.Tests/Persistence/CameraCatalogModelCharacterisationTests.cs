using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 353, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>CameraConfiguration.Configure</c> is split into
/// private methods. The refactor must leave the relational model exactly as it
/// was: any column, type, nullability, key, index or comparer that moves is a
/// schema or change-tracking change the existing migrations would not describe,
/// and nothing short of Integration.Tests (CI, real Postgres) would otherwise
/// notice.
///
/// <para>
/// Counterfactual this file was seen to catch: dropping
/// <c>.HasFilter("status &lt;&gt; 'Decommissioned'")</c> from
/// <c>ux_cameras_fab_name_normalized_active</c> changes the index line (and the
/// snapshot check); removing the <c>ValueComparer&lt;CameraName&gt;</c> argument
/// fails the comparer assertion.
/// </para>
///
/// <para>
/// <b>Why here.</b> This context has no <c>*.Infrastructure.Tests</c> project,
/// and a new test project is out of proportion for one class;
/// <c>Architecture.Tests</c> already references
/// <c>CameraCatalog.Infrastructure</c> and composes its <c>DbContext</c>
/// (spec 353 J3, after spec 350 J3).
/// </para>
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The
/// debug string embeds EF Core and Npgsql internals that shift on a patch bump,
/// which Dependabot merges weekly. This reads the same facts through the public
/// metadata API and states them as the table in <see cref="ExpectedModel"/>. The
/// table was cross-checked against the committed
/// <c>CameraCatalogDbContextModelSnapshot</c> entity by entity (Camera: seven
/// properties, the unique partial btree index, the table; Registration: owned,
/// shares the cameras table, key and foreign key on camera_id), and
/// <see cref="The_committed_model_snapshot_still_describes_the_model"/> asks EF
/// itself whether the snapshot and the model differ, so the check does not rest
/// on this test's own output.
/// </para>
/// </summary>
public class CameraCatalogModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "Camera -> table cameras (schema default), owned=False, key (camera_id)",
        "  constructor ()",
        "  camera_id [Id]: uuid; CameraIdentifier; not null; column not null; converted to Guid",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; column not null; max 32; converted to String",
        "  name [Name]: character varying(200); CameraName; not null; column not null; max 200; converted to String",
        "  name_normalized [NameNormalized]: character varying(200); String; null; column null; max 200; computed upper(name) stored; generated OnAddOrUpdate; shadow",
        "  rtsp_url [Url]: character varying(2048); RtspUrl; not null; column not null; max 2048; converted to String",
        "  status [Status]: character varying(32); CameraStatus; not null; column not null; max 32; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; column not null; converted to Int32; concurrency token",
        "  index ux_cameras_fab_name_normalized_active (fab, name_normalized) unique using btree where status <> 'Decommissioned'",
        "  navigation Registration -> Registration (reference), required dependent",
        "Registration -> table cameras (schema default), owned=True, key (camera_id)",
        "  constructor (RegisteredAt At, OperatorIdentifier By)",
        "  camera_id [CameraId]: uuid; CameraIdentifier; not null; column not null; converted to Guid; shadow",
        "  registered_at [At]: timestamp with time zone; RegisteredAt; not null; column not null; converted to DateTimeOffset",
        "  registered_by [By]: uuid; OperatorIdentifier; not null; column not null; converted to Guid",
        "  fk (camera_id) -> cameras (camera_id), required, ownership, on delete Cascade",
    ];

    private static readonly Lazy<Dictionary<string, string>> Constructors = new(BuildConstructors);

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);
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

        if (property.GetValueConverter() is { } converter)
        {
            details.Add($"converted to {TypeName(converter.ProviderClrType)}");
        }

        if (HasExplicitDefault(property))
        {
            details.Add($"default {property.GetDefaultValue()}");
        }

        if (property.GetComputedColumnSql() is { } computed)
        {
            details.Add($"computed {computed}{(property.GetIsStored() == true ? " stored" : string.Empty)}");
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
        + (index.GetMethod() is { } method ? $" using {method}" : string.Empty)
        + (index.GetFilter() is { } filter ? $" where {filter}" : string.Empty);

    private static IEnumerable<string> Describe(IEntityType entityType)
    {
        yield return $"{entityType.DisplayName()} -> table {entityType.GetTableName()}"
            + $" (schema {entityType.GetSchema() ?? "default"}), owned={entityType.IsOwned()},"
            + $" key ({Join(entityType.FindPrimaryKey()!.Properties.Select(property => Column(property)))})";

        yield return DescribeConstructor(entityType);

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

    private static string DescribeConstructor(IEntityType entityType) =>
        $"  constructor ({Constructors.Value[entityType.Name]})";

    private static Dictionary<string, string> BuildConstructors()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);
        Dictionary<string, string> constructors = [];
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var binding = ((RuntimeEntityType)entityType).ConstructorBinding as ConstructorBinding;
            constructors[entityType.Name] = binding is null
                ? "none"
                : Join(binding.Constructor.GetParameters().Select(parameter => $"{TypeName(parameter.ParameterType)} {parameter.Name}"));
        }

        return constructors;
    }

    [Fact]
    public void The_camera_catalog_model_has_exactly_this_shape()
    {
        var model = BuildModel();

        var actual = model.GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)
            .ToArray();

        actual.ShouldBe(ExpectedModel);
    }

    [Fact]
    public void The_name_comparer_tells_letter_case_apart_so_a_case_only_rename_is_detected()
    {
        var comparer = BuildModel()
            .FindEntityType(typeof(Camera))!
            .FindProperty(nameof(Camera.Name))!
            .GetValueComparer();

        comparer.Equals(CameraName.From("Line-3"), CameraName.From("line-3")).ShouldBeFalse();
        comparer.Equals(CameraName.From("Line-3"), CameraName.From("Line-3")).ShouldBeTrue();
        comparer.Equals(CameraName.From("Line-3"), CameraName.From("Line-4")).ShouldBeFalse();
    }

    [Fact]
    public void The_committed_model_snapshot_still_describes_the_model()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
