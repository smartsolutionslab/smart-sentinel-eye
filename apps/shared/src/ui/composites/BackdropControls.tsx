import { useEffect, useId, useRef } from 'react';
import { useListAllCameraChoicesQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import { ambiguousNamesOf, cameraLabel } from '../../format/cameraLabel.js';
import { Button } from '../primitives/Button.js';
import { FormField } from './FormField.js';
import type { CaptureState } from './useFrameCapture.js';

export type Backdrop = 'checkerboard' | 'captured' | 'white' | 'black';

export interface BackdropControlsProps {
  backdrop: Backdrop;
  onBackdropChange: (next: Backdrop) => void;
  hasCapturedFrame: boolean;
  /** Absent ⇒ the camera picker and capture control are not offered at all (FR-017's type-level degradation). */
  getToken?: () => Promise<string | null>;
  selectedCamera: string;
  onCameraChange: (next: string) => void;
  captureState: CaptureState;
  onCapture: () => void;
  onCancelCapture: () => void;
}

const BACKDROP_OPTIONS: ReadonlyArray<{ value: Backdrop; label: string }> = [
  { value: 'checkerboard', label: 'Checkerboard' },
  { value: 'white', label: 'White field' },
  { value: 'black', label: 'Black field' },
  { value: 'captured', label: 'Captured frame' },
];

// Spec 293 (issue #2342): Tailwind classes citing the semantic token layer,
// not inline styles.
const SELECT_CLASSNAME =
  'block w-full rounded-md border border-fg-muted bg-bg-elevated px-3 py-2 text-sm text-fg-primary ' +
  'placeholder:text-fg-muted focus-visible:outline-2 focus-visible:outline-offset-2 ' +
  'focus-visible:outline-focus-ring disabled:border-border-subtle disabled:text-fg-disabled';

/**
 * The backdrop selector plus the camera picker and capture control (spec
 * 147). The four-way selector is always offered — a translucent label can be
 * checked against a plain white or black field with no camera at all — and
 * costs no dependency of its own. The camera picker and "Capture frame" live
 * in `CameraCaptureSection` below, mounted only when `getToken` is supplied:
 * that is what keeps `useListAllCameraChoicesQuery` (an RTK Query hook,
 * requiring a `<Provider>`) from ever running for a caller that does not
 * offer the capture feature at all — `OverlayEditorCharacterisation.test.tsx`
 * renders `OverlayEditor` with no `getToken` and no Redux store, exactly as it
 * did before this spec (FR-017's degradation path expressed as a type).
 */
export function BackdropControls({
  backdrop,
  onBackdropChange,
  hasCapturedFrame,
  getToken,
  selectedCamera,
  onCameraChange,
  captureState,
  onCapture,
  onCancelCapture,
}: BackdropControlsProps) {
  // Radio grouping is document-wide, and this is a component in `apps/shared`
  // that can be mounted more than once on a page — a hardcoded `name` would
  // cross-wire two instances' radio groups (#2343/#2346 territory).
  const backdropGroupName = useId();

  return (
    <div className="mt-3 grid gap-3">
      <fieldset className="m-0 flex gap-4 border-0 p-0">
        <legend className="mb-1 p-0 text-sm font-medium text-fg-primary">Backdrop</legend>
        {BACKDROP_OPTIONS.map((option) => (
          <label key={option.value} className="flex items-center gap-1 text-sm text-fg-primary">
            <input
              type="radio"
              name={backdropGroupName}
              value={option.value}
              checked={backdrop === option.value}
              disabled={option.value === 'captured' && !hasCapturedFrame}
              onChange={() => onBackdropChange(option.value)}
              className="accent-accent"
            />
            {option.label}
          </label>
        ))}
      </fieldset>

      {getToken !== undefined && (
        <CameraCaptureSection
          selectedCamera={selectedCamera}
          onCameraChange={onCameraChange}
          captureState={captureState}
          onCapture={onCapture}
          onCancelCapture={onCancelCapture}
        />
      )}
    </div>
  );
}

interface CameraCaptureSectionProps {
  selectedCamera: string;
  onCameraChange: (next: string) => void;
  captureState: CaptureState;
  onCapture: () => void;
  onCancelCapture: () => void;
}

/**
 * The camera picker and capture control — split out from `BackdropControls`
 * so `useListAllCameraChoicesQuery` (which needs a Redux `<Provider>`) is
 * called only while this section is actually mounted, i.e. only when a
 * caller supplies `getToken`.
 */
function CameraCaptureSection({
  selectedCamera,
  onCameraChange,
  captureState,
  onCapture,
  onCancelCapture,
}: CameraCaptureSectionProps) {
  // Mirrors `LayoutEditorDialog.tsx:85-98`'s use of the same query, without
  // that dialog's name filter: this picker is a preview-time convenience, not
  // a record of anything the operator authors.
  const { data: cameras, isLoading: camerasLoading, isError: camerasFailed } = useListAllCameraChoicesQuery();
  const cameraSelectId = useId();

  const cameraItems = cameras?.items ?? [];
  const camerasTruncated = cameras !== undefined && !cameras.complete;
  // Fab-qualified when two fabs the operator holds share a name (issue
  // #2686) — the same rule `GridDesigner.tsx` applies to its tile picker.
  const ambiguousCameraNames = ambiguousNamesOf(cameraItems);
  const selectedCameraSummary = cameraItems.find((camera) => camera.cameraIdentifier === selectedCamera);
  const cameraName =
    selectedCameraSummary === undefined ? selectedCamera : cameraLabel(selectedCameraSummary, ambiguousCameraNames);

  // Spec 234 (issue #2356) FR-006/FR-007: focus must return to "Capture
  // frame" when a capture in flight ends (cancel, success, failure, or the
  // 10 s timeout) while "Cancel capture" held it — but only then. The
  // mechanism does not depend on `blur` firing when the Cancel button
  // unmounts (browsers differ on this); it tracks whether Cancel *had* focus
  // and, on the next exit from `capturing`, checks where focus actually
  // landed.
  const captureButtonRef = useRef<HTMLButtonElement>(null);
  const cancelWasFocusedRef = useRef(false);

  useEffect(() => {
    if (captureState === 'capturing') {
      cancelWasFocusedRef.current = false;
      return;
    }
    if (!cancelWasFocusedRef.current) return;
    cancelWasFocusedRef.current = false;
    if (document.activeElement === null || document.activeElement === document.body) {
      captureButtonRef.current?.focus();
    }
  }, [captureState]);

  return (
    <div className="grid gap-2">
      <FormField label="Camera" htmlFor={cameraSelectId}>
        <select
          id={cameraSelectId}
          className={SELECT_CLASSNAME}
          value={selectedCamera}
          onChange={(e) => onCameraChange(e.target.value)}
        >
          <option value="">{emptyCameraLabel(camerasLoading, camerasFailed, cameraItems.length === 0)}</option>
          {cameraItems.map((camera) => (
            <option key={camera.cameraIdentifier} value={camera.cameraIdentifier}>
              {cameraLabel(camera, ambiguousCameraNames)}
            </option>
          ))}
        </select>
      </FormField>
      {/* Spec 048's truncation notice, mirrored from LayoutEditorDialog — how
          many of how many, and stops there. */}
      {camerasTruncated && cameras !== undefined && (
        <p className="m-0 text-xs text-fg-muted">
          Showing {cameraItems.length} of {cameras.count} cameras.
        </p>
      )}
      <div className="flex gap-2">
        {/* Spec 293 §4.4: keeps native `disabled` — whether this should move
            to `unavailable` (ADR-0151) is spec 234's own mechanism, not
            reopened here. */}
        <Button
          ref={captureButtonRef}
          variant="secondary"
          onClick={onCapture}
          disabled={selectedCamera === '' || captureState === 'capturing'}
        >
          Capture frame
        </Button>
        {captureState === 'capturing' && (
          <Button
            variant="secondary"
            onClick={onCancelCapture}
            onFocus={() => {
              cancelWasFocusedRef.current = true;
            }}
          >
            Cancel capture
          </Button>
        )}
      </div>
      {/* Spec 234 (issue #2356) FR-005: always mounted, so a region inserted
          together with its content is not reliably announced — the same
          reason `ChainRecoveryNotice.tsx` keeps its own region present
          up front. Visible text, not `sr-only` (spec A3). */}
      <p aria-live="polite" data-testid="frame-capture-live-region" className="m-0 text-xs text-fg-muted">
        {captureState === 'capturing' ? `Capturing a frame from ${cameraName}…` : ''}
      </p>
      {captureState === 'failed' && (
        // #2365: unrelated to this file's own #2342 carve-out (colour tokens) —
        // a plain data-testid so a test can address this alert without an
        // unscoped role query, now that OverlayGeometryFields always mounts
        // four of its own in the same tree.
        <p role="alert" data-testid="frame-capture-alert" className="m-0 text-sm text-accent-fault">
          The frame could not be captured. The backdrop is unchanged — try again, or pick a different camera.
        </p>
      )}
    </div>
  );
}

function emptyCameraLabel(loading: boolean, failed: boolean, isEmpty: boolean): string {
  if (loading) return 'Loading cameras…';
  if (failed) return 'Cameras could not be loaded';
  if (isEmpty) return 'No cameras available';
  return 'Select a camera';
}
