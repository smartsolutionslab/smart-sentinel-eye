using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 350, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>OverlayConfiguration.Configure</c> is split into
/// private methods and before <c>OverlayElement</c>'s private constructor takes
/// a parameter object. The refactor must leave the relational model exactly as
/// it was: any column, type, nullability, key, foreign key or index that moves
/// is a schema change the existing migrations would not describe, and nothing
/// short of Integration.Tests (CI, real Postgres) would otherwise notice.
///
/// <para>
/// Counterfactual this file was seen to catch: dropping
/// <c>.HasFilter("archived_at IS NULL")</c> from <c>ux_overlays_name_active</c>
/// changes the index line; renaming the EF constructor's <c>color</c> parameter
/// breaks the constructor-binding line (and the model build with it).
/// </para>
///
/// <para>
/// <b>Why here.</b> The sibling contexts keep this in their own
/// <c>*.Infrastructure.Tests</c> project. This context has none, and a new test
/// project is out of proportion for one class; <c>Architecture.Tests</c> already
/// references <c>OverlayDesigner.Infrastructure</c> and composes its
/// <c>DbContext</c> (spec 350 J3).
/// </para>
///
/// <para>
/// <b>Why the logical shape rather than <c>Model.ToDebugString()</c>.</b> The
/// debug string embeds EF Core and Npgsql internals that shift on a patch bump,
/// which Dependabot merges weekly. This reads the same facts through the public
/// metadata API and states them as the table in <see cref="ExpectedModel"/>: per
/// entity its table and primary key, per property its column, store type, CLR
/// type, nullability, length, converter, default and concurrency flag, then its
/// foreign keys, indexes and navigations, and which constructor EF will
/// materialise the entity through. The table was cross-checked against the
/// committed <c>OverlayDesignerDbContextModelSnapshot</c> entity by entity, and
/// <see cref="The_committed_model_snapshot_still_describes_the_model"/> asks EF
/// itself whether the snapshot and the model differ, so the check does not rest
/// on this test's own output.
/// </para>
/// </summary>
public class OverlayDesignerModelCharacterisationTests
{
    private static readonly string[] ExpectedModel =
    [
        "NormalizedPosition -> table overlay_revision_elements (schema default), owned=True, key (revision_id, ordinal)",
        "  constructor (Decimal X, Decimal Y)",
        "  ordinal [OverlayElementordinal]: integer; Int32; not null; column not null; shadow",
        "  revision_id [OverlayElementrevision_id]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid; shadow",
        "  x [X]: numeric; Decimal; not null; column not null",
        "  y [Y]: numeric; Decimal; not null; column not null",
        "  fk (revision_id, ordinal) -> overlay_revision_elements (revision_id, ordinal), required, ownership, on delete Cascade",
        "NormalizedSize -> table overlay_revision_elements (schema default), owned=True, key (revision_id, ordinal)",
        "  constructor (Decimal Width, Decimal Height)",
        "  height [Height]: numeric; Decimal; not null; column not null",
        "  ordinal [OverlayElementordinal]: integer; Int32; not null; column not null; shadow",
        "  revision_id [OverlayElementrevision_id]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid; shadow",
        "  width [Width]: numeric; Decimal; not null; column not null",
        "  fk (revision_id, ordinal) -> overlay_revision_elements (revision_id, ordinal), required, ownership, on delete Cascade",
        "Overlay -> table overlays (schema default), owned=False, key (overlay_id)",
        "  constructor ()",
        "  archived_at [ArchivedAt]: timestamp with time zone; ArchivedAt; null; column null; converted to DateTimeOffset",
        "  name [Name]: character varying(80); OverlayName; not null; column not null; max 80; converted to String",
        "  overlay_id [Id]: uuid; OverlayIdentifier; not null; column not null; converted to Guid",
        "  version [Version]: integer; AggregateVersion; not null; column not null; converted to Int32; concurrency token",
        "  index ux_overlays_name_active (name) unique where archived_at IS NULL",
        "  navigation Creation -> Overlay.Creation#Creation (reference), required dependent",
        "  navigation Revisions -> Revision (collection)",
        "Overlay.Creation#Creation -> table overlays (schema default), owned=True, key (overlay_id)",
        "  constructor (CreatedAt At, OperatorIdentifier By)",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; column not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; column not null; converted to Guid",
        "  overlay_id [OverlayId]: uuid; OverlayIdentifier; not null; column not null; converted to Guid; shadow",
        "  fk (overlay_id) -> overlays (overlay_id), required, ownership, on delete Cascade",
        "OverlayElement -> table overlay_revision_elements (schema default), owned=True, key (revision_id, ordinal)",
        "  constructor (ElementKind kind, OverlayColor color)",
        "  color [Color]: character varying(9); OverlayColor; not null; column not null; max 9; converted to String",
        "  kind [Kind]: character varying(16); ElementKind; not null; column not null; max 16; converted to String",
        "  ordinal [ordinal]: integer; Int32; not null; column not null",
        "  revision_id [revision_id]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid; shadow",
        "  fk (revision_id) -> overlay_revisions (revision_id), required, ownership, on delete Cascade",
        "  navigation Position -> NormalizedPosition (reference), required dependent",
        "  navigation Size -> NormalizedSize (reference), required dependent",
        "  navigation Text -> TextContent (reference)",
        "Revision -> table overlay_revisions (schema default), owned=True, key (revision_id)",
        "  constructor ()",
        "  archived_at [ArchivedAt]: timestamp with time zone; ArchivedAt; null; column null; converted to DateTimeOffset",
        "  overlay_id [overlay_id]: uuid; OverlayIdentifier; not null; column not null; converted to Guid; shadow",
        "  published_at [PublishedAt]: timestamp with time zone; PublishedAt; null; column null; converted to DateTimeOffset",
        "  revision_id [Id]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid",
        "  revision_number [Number]: integer; OverlayRevisionNumber; not null; column not null; converted to Int32",
        "  state [State]: character varying(16); OverlayRevisionState; not null; column not null; max 16; converted to String",
        "  fk (overlay_id) -> overlays (overlay_id), required, ownership, on delete Cascade",
        "  index ux_overlay_revisions_number (overlay_id, revision_number) unique",
        "  index ux_overlay_revisions_one_published (overlay_id) unique where state = 'Published'",
        "  navigation Creation -> Revision.Creation#Creation (reference), required dependent",
        "  navigation Elements -> OverlayElement (collection)",
        "Revision.Creation#Creation -> table overlay_revisions (schema default), owned=True, key (revision_id)",
        "  constructor (CreatedAt At, OperatorIdentifier By)",
        "  created_at [At]: timestamp with time zone; CreatedAt; not null; column not null; converted to DateTimeOffset",
        "  created_by [By]: uuid; OperatorIdentifier; not null; column not null; converted to Guid",
        "  revision_id [RevisionId]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid; shadow",
        "  fk (revision_id) -> overlay_revisions (revision_id), required, ownership, on delete Cascade",
        "TextContent -> table overlay_revision_elements (schema default), owned=True, key (revision_id, ordinal)",
        "  constructor (String Value, Int32 FontSizePx)",
        "  font_size_px [FontSizePx]: integer; Int32; not null; column null",
        "  ordinal [OverlayElementordinal]: integer; Int32; not null; column not null; shadow",
        "  revision_id [OverlayElementrevision_id]: uuid; OverlayRevisionIdentifier; not null; column not null; converted to Guid; shadow",
        "  text [Value]: character varying(256); String; not null; column null; max 256",
        "  fk (revision_id, ordinal) -> overlay_revision_elements (revision_id, ordinal), required, ownership, on delete Cascade",
    ];

    private static readonly Lazy<Dictionary<string, string>> Constructors = new(BuildConstructors);

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<OverlayDesignerDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using OverlayDesignerDbContext context = new(options);
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
        var options = new DbContextOptionsBuilder<OverlayDesignerDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using OverlayDesignerDbContext context = new(options);
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
    public void The_overlay_designer_model_has_exactly_this_shape()
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
        var options = new DbContextOptionsBuilder<OverlayDesignerDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using OverlayDesignerDbContext context = new(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
