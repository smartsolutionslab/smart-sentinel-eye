/**
 * Marks a tile the wall could not hold to its common instant (spec 045 US3,
 * FR-012).
 *
 * <p>
 * <b>The tile keeps playing.</b> The wall gives up its claim about this tile,
 * never the picture — an operator does not lose a camera because the system
 * could not synchronise it (FR-012b). This badge is the difference between a
 * wall that is misaligned and a wall that is misaligned <em>and says so</em>,
 * which is the whole of US3: a silently misaligned wall looks exactly like an
 * aligned one.
 * </p>
 */
export function TileAlignmentBadge({ camera }: { camera: string }) {
  return (
    <div
      role="status"
      data-testid="tile-out-of-alignment"
      data-camera={camera}
      className="absolute bottom-2 left-1/2 z-10 -translate-x-1/2 rounded-md bg-accent-warning/30 px-3 py-1 text-xs text-accent-warning"
    >
      Not in sync with the wall
    </div>
  );
}
