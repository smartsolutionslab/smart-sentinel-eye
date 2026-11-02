/**
 * Counts a sampler failure and answers the running count when this one is worth a
 * line, or null.
 *
 * The decade cadence #2084 landed for the fab-less frame (`CellPage.tsx`
 * `countReportableSkew`), minus that case's fab predicate. A wall runs for weeks: a
 * line per tick evicts the first — the diagnostically valuable — occurrence from any
 * console buffer, and a line per session is emitted before anyone is looking.
 *
 * Duplicated rather than shared with CellPage deliberately; see spec 140
 * §"The cadence is #2084's decade curve". Extract it at the third site.
 */
export function countReportableFailure(counter: { current: number }): number | null {
  counter.current += 1;
  const count = counter.current;
  let decade = 1;
  while (decade < count) decade *= 10;
  return decade === count ? count : null;
}

/** The message of a thrown value, or its string form when it is not an Error. */
export function reasonFrom(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
