import clsx from 'clsx';
import type { CameraViewerStatus } from '../useWhepSession.js';
import { hintFor } from './viewerStatus.js';

export function ViewerOverlay({
  status,
  message,
  queryError,
  failedRead,
  label,
}: {
  status: CameraViewerStatus;
  message: string | null;
  queryError: unknown;
  failedRead: boolean;
  label: string;
}) {
  const tone =
    failedRead || status === 'error' || status === 'offline'
      ? 'text-accent-fault-on-video'
      : status === 'reconnecting'
        ? 'text-accent-warning-on-video'
        : 'text-fg-muted-on-video';

  const hint = hintFor(message, queryError);

  return (
    // FR-006: hidden from the accessibility tree so the message is read once,
    // from `CameraViewer`'s status region — not twice, once from each. Still
    // visible on screen; `aria-hidden` affects only assistive tech.
    <div
      aria-hidden="true"
      className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-scrim text-center text-sm"
    >
      <span className={clsx('font-medium', tone)}>{label}</span>
      {hint !== null && <span className="px-4 text-xs text-fg-muted-on-video">{hint}</span>}
    </div>
  );
}
