import type { CameraSummary } from '../api/cameras.api.js';

/**
 * How a camera is labelled anywhere its name is shown to an operator.
 *
 * <p>
 * Names are unique only <b>within</b> a fab, and both consumers of this
 * helper — `GridDesigner`'s tile picker and `BackdropControls`'s capture
 * picker — list cameras across every fab the operator holds. Two
 * <c>Line-1-Entrance</c> cameras from two different fabs would otherwise
 * appear as identical, indistinguishable options (issue #2686; the rule
 * itself is spec 055 / issue #2607, first built in `GridDesigner.tsx` and
 * moved here so a second consumer is not stuck re-deriving it).
 * </p>
 *
 * <p>
 * Qualified only when it has to be. Most operators hold one fab, and
 * appending it to every option would be noise in the common case to serve
 * the rare one.
 * </p>
 */
export function cameraLabel(camera: CameraSummary, ambiguousNames: ReadonlySet<string>): string {
  return ambiguousNames.has(camera.name) ? `${camera.name} (${camera.fab})` : camera.name;
}

/** Names held by more than one camera, which is possible across fabs. */
export function ambiguousNamesOf(cameras: ReadonlyArray<CameraSummary>): ReadonlySet<string> {
  const seen = new Set<string>();
  const twice = new Set<string>();
  for (const camera of cameras) {
    if (seen.has(camera.name)) twice.add(camera.name);
    seen.add(camera.name);
  }
  return twice;
}
