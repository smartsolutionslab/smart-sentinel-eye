using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Per-edit revision within a Layout chain. Owned by the
/// <see cref="Layout"/> aggregate; mutators are package-internal so the
/// aggregate is the sole entry point — keeps the
/// at-most-one-Published-per-chain invariant inside the aggregate
/// transaction.
///
/// <para>
/// Spec 010 (ADR-0112): a revision carries a <see cref="GridDimensions"/>
/// and a non-empty, in-bounds set of <see cref="Tile"/>s instead of a
/// single camera + optional overlay. A single-camera layout is a 1-tile
/// wall on a 1×1 grid. The grid invariants are validated by the owning
/// aggregate before the tile set is set here.
/// </para>
/// </summary>
public sealed class Revision
{
    private readonly List<Tile> tiles = [];

    public LayoutRevisionIdentifier Id { get; private set; }

    public LayoutRevisionNumber Number { get; private set; }

    public LayoutRevisionState State { get; private set; } = null!;

    public GridDimensions Grid { get; private set; } = null!;

    /// <summary>
    /// The non-empty, in-bounds set of tiles composited on this revision
    /// (spec 010). Replaced atomically via <see cref="ReplaceTiles"/> —
    /// there is no per-tile mutator.
    /// </summary>
    public IReadOnlyList<Tile> Tiles => tiles;

    public Creation Creation { get; private set; } = null!;

    public PublishedAt? PublishedAt { get; private set; }

    public ArchivedAt? ArchivedAt { get; private set; }

    private Revision() { }

    internal static Revision NewDraft(
        LayoutRevisionNumber number,
        GridDimensions grid,
        IReadOnlyList<Tile> tiles,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy)
    {
        Revision revision = new()
        {
            Id = LayoutRevisionIdentifier.New(),
            Number = number,
            State = LayoutRevisionState.Draft,
            Grid = new GridDimensions(grid.Rows, grid.Cols),
            Creation = Creation.From(CreatedAt.From(createdAt), createdBy),
        };
        // Clone the owned entities (Grid above + each tile below) so the
        // revision owns fresh instances. BranchDraft copies a Published
        // revision's Grid + tiles, which EF already tracks as owned entities
        // under that revision; reusing the same instances would make EF see
        // one owned entity under two owners and throw on save. Cloning is a
        // harmless copy on the create path (fresh grid + tiles).
        foreach (Tile tile in tiles)
        {
            revision.tiles.Add(new Tile(tile.Camera, tile.Overlay, tile.Position));
        }

        return revision;
    }

    internal static Revision Branch(
        LayoutRevisionNumber number,
        GridDimensions grid,
        IReadOnlyList<Tile> tiles,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        NewDraft(number, grid, tiles, createdAt, createdBy);

    internal void Publish(DateTimeOffset publishedAt)
    {
        if (State != LayoutRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Published.");
        }
        State = LayoutRevisionState.Published;
        PublishedAt = PublishedAt.From(publishedAt);
    }

    internal void Revert()
    {
        if (State != LayoutRevisionState.Published)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Draft (Revert).");
        }
        State = LayoutRevisionState.Draft;
        PublishedAt = null;
    }

    /// <summary>
    /// Atomically replaces this Draft revision's grid + entire tile set
    /// (spec 010). The aggregate validates the grid invariants before
    /// calling this; only the Draft-state guard lives here (a programmer
    /// error to edit a non-Draft revision — throws as before).
    /// </summary>
    internal void ReplaceTiles(GridDimensions grid, IReadOnlyList<Tile> tiles)
    {
        if (State != LayoutRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} is {State}; only Draft revisions are editable.");
        }
        Grid = grid;
        this.tiles.Clear();
        this.tiles.AddRange(tiles);
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (State == LayoutRevisionState.Archived)
        {
            // Idempotent — re-archiving an already-Archived revision is a no-op.
            return;
        }
        State = LayoutRevisionState.Archived;
        ArchivedAt = ArchivedAt.From(archivedAt);
    }
}
