using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Overlay"/> aggregate (spec 004).
///
/// <para>
/// Mirrors LayoutComposition's LayoutConfiguration: revisions are an
/// owned collection mapped to <c>overlay_revisions</c>; the partial
/// unique index on <c>state = 'Published'</c> backs the aggregate's
/// at-most-one-Published invariant as a belt-and-braces guard.
/// </para>
///
/// <para>
/// Two partial unique indexes, not one. The second —
/// <c>ux_overlays_name_active</c> — backs FR-006, one live chain per name,
/// and needs <c>overlays.archived_at</c> to exist at all: the rule is about
/// the chain's revisions, and an index predicate can read only its own row
/// (spec 086 §1.1). The column is the aggregate's answer written down, not a
/// cache — <c>Overlay.RecomputeArchival</c> restates it after every mutation.
/// </para>
///
/// <para>
/// Spec 150 (#2345): a revision now carries an ordered, non-empty set of
/// 1..8 <see cref="OverlayElement"/>s instead of one. Elements are mapped as
/// a nested owned collection on <c>overlay_revision_elements</c> (spec 300,
/// #2349, ADR-0165 — renamed from <c>overlay_revision_labels</c>), keyed
/// <c>(revision_id, ordinal)</c> — the join LayoutComposition already pays
/// for <c>layout_revision_tiles</c>.
/// </para>
/// </summary>
public sealed class OverlayConfiguration : IEntityTypeConfiguration<Overlay>
{
    public void Configure(EntityTypeBuilder<Overlay> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("overlays");
        builder.HasKey(overlay => overlay.Id);

        builder.Property(overlay => overlay.Id)
            .HasColumnName("overlay_id")
            .HasConversion(id => id.Value, value => OverlayIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(overlay => overlay.Name)
            .HasColumnName("name")
            .HasMaxLength(OverlayName.MaximumLength)
            .HasConversion(name => name.Value, value => OverlayName.From(value))
            .IsRequired();

        // Owned reference onto the two columns the pair occupied. The
        // Navigation(...).IsRequired() line keeps them NOT NULL (#2022).
        builder.OwnsOne(overlay => overlay.Creation, creation =>
        {
            creation.Property(value => value.At)
                .HasColumnName("created_at")
                .HasConversion(at => at.Value, value => CreatedAt.From(value))
                .IsRequired();

            creation.Property(value => value.By)
                .HasColumnName("created_by")
                .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                .IsRequired();
        });
        builder.Navigation(overlay => overlay.Creation).IsRequired();

        builder.Property(overlay => overlay.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        // The chain's own archival, materialised onto the parent row. The
        // predicate it summarises - every revision Archived - lives on
        // overlay_revisions, and a Postgres index predicate may not read another
        // table, so the only way the database can enforce the name rule is to
        // have the answer on the row the name is on (spec 086 §1.1).
        builder.Property(overlay => overlay.ArchivedAt)
            .HasColumnName("archived_at")
            .HasConversion(at => at!.Value, value => ArchivedAt.From(value))
            .IsRequired(false);

        // FR-006 in the database rather than only in
        // CreateOverlayDraftCommandHandler: one live chain per name. Replaces
        // ix_overlays_name, a plain btree that read like a constraint without
        // being one.
        //
        // Partial, and deliberately so. A total unique index would satisfy every
        // race test and quietly turn archiving into permanent confiscation of
        // the word — the reuse clause FR-006 spells out, and the same thing
        // RuleConfiguration and VariableConfiguration each ask the next reader
        // not to take away.
        builder.HasIndex(overlay => overlay.Name)
            .HasDatabaseName("ux_overlays_name_active")
            .IsUnique()
            .HasFilter("archived_at IS NULL");

        builder.OwnsMany(overlay => overlay.Revisions, revisions =>
        {
            revisions.ToTable("overlay_revisions");
            revisions.WithOwner().HasForeignKey("overlay_id");
            revisions.HasKey(revision => revision.Id);

            revisions.Property(revision => revision.Id)
                .HasColumnName("revision_id")
                .HasConversion(id => id.Value, value => OverlayRevisionIdentifier.From(value))
                .ValueGeneratedNever();

            revisions.Property(revision => revision.Number)
                .HasColumnName("revision_number")
                .HasConversion(number => number.Value, value => OverlayRevisionNumber.From(value))
                .IsRequired();

            revisions.Property(revision => revision.State)
                .HasColumnName("state")
                .HasMaxLength(16)
                .HasConversion(state => state.Value, value => OverlayRevisionState.From(value))
                .IsRequired();

            // Elements are a nested owned collection, one level deeper than
            // anything else in this feature: a composite value object
            // (OverlayElement) owning two more composite value objects
            // (Position, Size) plus an optional one (Text), itself owned by
            // the revisions collection. An element has no identity of its
            // own, so the composite key is (revision_id, ordinal) — the
            // element's ElementOrdinal, flattened into the scalar mapped
            // below (spec 150 FR-005). Spec 300 (#2349, ADR-0165) widened
            // the table from text-only labels to a closed kind + colour
            // model; the migration (T009) renames the physical table from
            // <c>overlay_revision_labels</c> to <c>overlay_revision_elements</c>
            // and its columns to match this mapping.
            revisions.OwnsMany(revision => revision.Elements, elements =>
            {
                elements.ToTable("overlay_revision_elements");
                elements.WithOwner().HasForeignKey("revision_id");

                // Field-backed, not a CLR property: the element exposes only
                // its ElementOrdinal value object, so the scalar the key
                // needs is mapped onto its private field rather than
                // published as an int on a domain type (constitution §II).
                // The name must be the field name exactly — EF refuses a
                // field-only property whose name differs
                // (LayoutConfiguration's row/col comment records the same
                // cost).
                elements.Property<int>("ordinal").HasColumnName("ordinal").IsRequired().ValueGeneratedNever();
                elements.HasKey("revision_id", "ordinal");
                elements.Ignore(element => element.Ordinal);

                elements.Property(element => element.Kind)
                    .HasColumnName("kind")
                    .HasMaxLength(16)
                    .HasConversion(kind => kind.Value, value => ElementKind.From(value))
                    .IsRequired();

                elements.Property(element => element.Color)
                    .HasColumnName("color")
                    .HasMaxLength(9)
                    .HasConversion(color => color.Value, value => OverlayColor.From(value))
                    .IsRequired();

                // The four columns stay where they were — the owned-reference
                // default would name them Position_X and make them nullable,
                // which is #2022's shape, so both the column name and the
                // Navigation(...).IsRequired() below are load-bearing.
                elements.OwnsOne(element => element.Position, position =>
                {
                    position.Property(value => value.X).HasColumnName("x").IsRequired();
                    position.Property(value => value.Y).HasColumnName("y").IsRequired();
                });
                elements.Navigation(element => element.Position).IsRequired();

                elements.OwnsOne(element => element.Size, size =>
                {
                    size.Property(value => value.Width).HasColumnName("width").IsRequired();
                    size.Property(value => value.Height).HasColumnName("height").IsRequired();
                });
                elements.Navigation(element => element.Size).IsRequired();

                // Text is the one optional owned reference in this
                // configuration (#2022's Position/Size comment says NOT
                // NULL is load-bearing there; here it is the opposite,
                // deliberately): present iff Kind is Text (ADR-0165 §1), so
                // there is no Navigation(...).IsRequired() call and its two
                // columns are nullable.
                elements.OwnsOne(element => element.Text, text =>
                {
                    text.Property(value => value.Value)
                        .HasColumnName("text")
                        .HasMaxLength(TextContent.MaximumTextLength);

                    text.Property(value => value.FontSizePx)
                        .HasColumnName("font_size_px");
                });
            });

            // The nested case: a composite inside an owned collection, one
            // level deeper than anything else in this feature. The columns
            // land on the revisions table exactly as the pair did.
            revisions.OwnsOne(revision => revision.Creation, creation =>
            {
                creation.Property(value => value.At)
                    .HasColumnName("created_at")
                    .HasConversion(at => at.Value, value => CreatedAt.From(value))
                    .IsRequired();

                creation.Property(value => value.By)
                    .HasColumnName("created_by")
                    .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                    .IsRequired();
            });
            revisions.Navigation(revision => revision.Creation).IsRequired();

            revisions.Property(revision => revision.PublishedAt)
                .HasColumnName("published_at")
            .HasConversion(v => v!.Value, value => PublishedAt.From(value))
                .IsRequired(false);

            revisions.Property(revision => revision.ArchivedAt)
                .HasColumnName("archived_at")
            .HasConversion(v => v!.Value, value => ArchivedAt.From(value))
                .IsRequired(false);

            revisions.HasIndex("overlay_id", nameof(Revision.Number))
                .HasDatabaseName("ux_overlay_revisions_number")
                .IsUnique();

            revisions.HasIndex("overlay_id")
                .HasDatabaseName("ux_overlay_revisions_one_published")
                .IsUnique()
                .HasFilter("state = 'Published'");
        });

        builder.Ignore(overlay => overlay.PendingEvents);
    }
}
