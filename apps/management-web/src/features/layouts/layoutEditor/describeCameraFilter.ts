interface CameraFilterStatusInput {
  camerasFetching: boolean;
  filterSettling: boolean;
  camerasFailed: boolean;
  filtering: boolean;
  camerasTruncated: boolean;
  cameraItems: readonly unknown[];
  cameraFilter: string;
  cameras: { complete: boolean; count: number } | undefined;
}

// The order matters: "searching" before "nothing matched", or a slow response
// shows an operator "no cameras match" about a search still running — the one
// wrong answer this state exists to prevent.
export function describeCameraFilter({
  camerasFetching,
  filterSettling,
  camerasFailed,
  filtering,
  camerasTruncated,
  cameraItems,
  cameraFilter,
  cameras,
}: CameraFilterStatusInput): string {
  if (camerasFetching || filterSettling) return 'Searching…';
  if (camerasFailed) return 'The camera list could not be loaded.';
  // Unfiltered, this line said "N cameras." directly beneath a truncation
  // notice already reading "Showing 200 of 600 cameras." — two totals for one
  // list, adjacent, disagreeing. The notice is the more informative of the
  // two, so this one stands down rather than competing with it.
  if (!filtering) return camerasTruncated ? '' : `${cameraItems.length} cameras.`;
  if (cameraItems.length === 0) return `No camera matches “${cameraFilter.trim()}”.`;

  // **Not `cameras.count`.** On a mid-walk page failure the client sets that
  // to a deliberate sentinel (`gathered + 1`) meaning "there is more, and I do
  // not know how much" — rendering it here would read as a match count and
  // put a fabricated number in front of an operator. When the walk is
  // incomplete the honest statement is how many were found, with no total.
  return cameras?.complete === true
    ? `${cameraItems.length} of ${cameras.count} cameras match.`
    : `${cameraItems.length} matches so far — the list is incomplete.`;
}
