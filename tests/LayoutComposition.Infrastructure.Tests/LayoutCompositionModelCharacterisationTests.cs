using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 345, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>LayoutConfiguration.Configure</c> and
/// <c>WallConfiguration.Configure</c> are split into private methods. The
/// refactor must leave the relational model exactly as it was: any column,
/// type, nullability, key, foreign key or index that moves is a schema change
/// the existing migrations would not describe, and nothing short of
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
/// foreign keys and indexes.
/// </para>
///
/// <para>
/// A column added, removed or renamed, a type or nullability changed, a key
/// altered or an index dropped changes a line below and fails
/// <see cref="The_layout_composition_model_has_exactly_this_shape"/>.
/// </para>
/// </summary>
public class LayoutCompositionModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "GridDimensions -> table layout_revisions (schema default), owned=True, key (RevisionId)",
        "  RevisionId [RevisionId]: uuid; LayoutRevisionIdentifier; not null; converted to Guid; shadow",
        "  grid_cols [Cols]: integer; Int32; not null",
        "  grid_rows [Rows]: integer; Int32; not null",
        "  fk (RevisionId) -> layout_revisions (revision_id), required, ownership, on delete Cascade",
        "Layout -> table layouts (schema default), owned=False, key (layout_id)",
        "  archived_at [ArchivedAt]: timestamp with time zone; ArchivedAt; null; converted to DateTimeOffset",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  layout_id [Id]: uuid; LayoutIdentifier; not null; converted to Guid",
        "  name [Name]: character varying(80); LayoutName; not null; max 80; converted to String",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  index ix_layouts_fab (fab)",
        "  index ux_layouts_fab_name_active (fab, name) unique where archived_at IS NULL",
        "  navigation Creation -> Layout.Creation#Creation (reference), required dependent",
        "  navigation Revisions -> Revision (collection)",
        "Layout.Creation#Creation -> table layouts (schema default), owned=True, key (LayoutId)",
        "  LayoutId [LayoutId]: uuid; LayoutIdentifier; not null; converted to Guid; shadow",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (LayoutId) -> layouts (layout_id), required, ownership, on delete Cascade",
        "Revision -> table layout_revisions (schema default), owned=True, key (revision_id)",
        "  archived_at [ArchivedAt]: timestamp with time zone; ArchivedAt; null; converted to DateTimeOffset",
        "  layout_id [layout_id]: uuid; LayoutIdentifier; not null; converted to Guid; shadow",
        "  published_at [PublishedAt]: timestamp with time zone; PublishedAt; null; converted to DateTimeOffset",
        "  revision_id [Id]: uuid; LayoutRevisionIdentifier; not null; converted to Guid",
        "  revision_number [Number]: integer; LayoutRevisionNumber; not null; converted to Int32",
        "  state [State]: character varying(16); LayoutRevisionState; not null; max 16; converted to String",
        "  fk (layout_id) -> layouts (layout_id), required, ownership, on delete Cascade",
        "  index ux_layout_revisions_number (layout_id, revision_number) unique",
        "  index ux_layout_revisions_one_published (layout_id) unique where state = 'Published'",
        "  navigation Creation -> Revision.Creation#Creation (reference), required dependent",
        "  navigation Grid -> GridDimensions (reference), required dependent",
        "  navigation Tiles -> Tile (collection)",
        "Revision.Creation#Creation -> table layout_revisions (schema default), owned=True, key (RevisionId)",
        "  RevisionId [RevisionId]: uuid; LayoutRevisionIdentifier; not null; converted to Guid; shadow",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (RevisionId) -> layout_revisions (revision_id), required, ownership, on delete Cascade",
        "Tile -> table layout_revision_tiles (schema default), owned=True, key (revision_id, row, col)",
        "  camera_id [Camera]: uuid; CameraIdentifier; not null; converted to Guid",
        "  col [col]: integer; Int32; not null",
        "  col_span [colSpan]: integer; Int32; not null; default 1; generated OnAdd",
        "  overlay_id [OverlayValue]: uuid; OverlayIdentifier?; null; converted to Guid?",
        "  revision_id [revision_id]: uuid; LayoutRevisionIdentifier; not null; converted to Guid; shadow",
        "  row [row]: integer; Int32; not null",
        "  row_span [rowSpan]: integer; Int32; not null; default 1; generated OnAdd",
        "  fk (revision_id) -> layout_revisions (revision_id), required, ownership, on delete Cascade",
        "  index ix_layout_revision_tiles_overlay_id (overlay_id)",
        "Wall -> table walls (schema default), owned=False, key (wall_id)",
        "  fab [Fab]: character varying(32); FabIdentifier; not null; max 32; converted to String",
        "  name [Name]: character varying(80); WallName; not null; max 80; converted to String",
        "  scene_version [SceneVersion]: bigint; SceneVersion; not null; converted to Int64",
        "  scenes [Scenes]: uuid[]; IReadOnlyList<LayoutIdentifier>; not null; converted to Guid[]",
        "  showing_layout_id [Showing]: uuid; LayoutIdentifier; not null; converted to Guid",
        "  showing_since [ShowingSince]: timestamp with time zone; ShowingSince; not null; converted to DateTimeOffset",
        "  version [Version]: integer; AggregateVersion; not null; converted to Int32; concurrency token",
        "  wall_id [Id]: uuid; WallIdentifier; not null; converted to Guid",
        "  index ux_walls_fab_name_ci (fab, name) unique",
        "  navigation Creation -> Wall.Creation#Creation (reference), required dependent",
        "Wall.Creation#Creation -> table walls (schema default), owned=True, key (WallId)",
        "  WallId [WallId]: uuid; WallIdentifier; not null; converted to Guid; shadow",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; converted to Guid",
        "  fk (WallId) -> walls (wall_id), required, ownership, on delete Cascade",
    ];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<LayoutCompositionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using LayoutCompositionDbContext context = new(options);
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
    public void The_layout_composition_model_has_exactly_this_shape()
    {
        var model = BuildModel();

        var actual = model.GetEntityTypes()
            .OrderBy(entityType => entityType.DisplayName(), StringComparer.Ordinal)
            .SelectMany(Describe)
            .ToArray();

        actual.ShouldBe(ExpectedModel);
    }
}
