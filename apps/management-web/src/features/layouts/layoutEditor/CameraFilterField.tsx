import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';

const cameraFilterStatusId = 'layout-camera-filter-status';

interface CameraFilterFieldProps {
  cameraFilter: string;
  setCameraFilter: (value: string) => void;
  camerasTruncated: boolean;
  shownCount: number;
  totalCount: number | undefined;
  cameraNoticeId: string | undefined;
  cameraFilterStatus: string;
}

export function CameraFilterField({
  cameraFilter,
  setCameraFilter,
  camerasTruncated,
  shownCount,
  totalCount,
  cameraNoticeId,
  cameraFilterStatus,
}: CameraFilterFieldProps) {
  return (
    <>
      {/*
        Spec 048's truncation notice. It says how many of how many, and stops
        there — the gap can also come from a camera retired between requests,
        so "the rest cannot be chosen" would sometimes be wrong.
      */}
      {camerasTruncated && (
        <p id={cameraNoticeId} className="text-sm text-fg-muted">
          Showing {shownCount} of {totalCount} cameras.
        </p>
      )}
      {/*
        Spec 055. A field beside the native lists rather than a combobox
        replacing them: the selects already carry role and value
        announcement, arrow-key movement, Escape and start-of-name
        type-ahead. Replacing them would mean re-implementing all of it, and
        losing any of it is invisible to anyone testing with a mouse.
      */}
      <FormField label="Find a camera" htmlFor="layout-camera-filter">
        <Input
          id="layout-camera-filter"
          type="search"
          placeholder="Part of a name, anywhere in it"
          value={cameraFilter}
          onChange={(event) => setCameraFilter(event.target.value)}
          aria-describedby={cameraFilterStatusId}
        />
      </FormField>
      {/*
        **Three states, told apart.** An operator who cannot distinguish
        "nothing matched" from "still loading" concludes the camera is gone
        and registers a duplicate — which is refused, because names are
        unique, so they are then stuck. `aria-live` because the list shrinking
        silently is the same problem for anyone not watching it.
      */}
      <p id={cameraFilterStatusId} aria-live="polite" className="text-sm text-fg-muted">
        {cameraFilterStatus}
      </p>
    </>
  );
}
