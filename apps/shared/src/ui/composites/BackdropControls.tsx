import type { CSSProperties } from 'react';
import { useListAllCameraChoicesQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
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

// Inline styles, matching the rest of `OverlayEditor.tsx` — not Tailwind, not
// tokens. #2342 converts this whole file to the token system in one pass; a
// half-converted component would make it reconcile two idioms instead.
const FIELDSET_STYLE: CSSProperties = { display: 'flex', gap: 16, border: 'none', padding: 0, margin: 0 };
const LEGEND_STYLE: CSSProperties = { fontSize: 14, padding: 0, marginBottom: 4 };
const RADIO_LABEL_STYLE: CSSProperties = { display: 'flex', alignItems: 'center', gap: 4, fontSize: 14 };
const SELECT_STYLE: CSSProperties = { padding: 8, fontSize: 14 };
const BUTTON_STYLE: CSSProperties = { padding: '8px 12px', fontSize: 14 };
const NOTICE_STYLE: CSSProperties = { fontSize: 12, color: '#6b7280', margin: 0 };
const ALERT_STYLE: CSSProperties = { fontSize: 13, color: '#b91c1c', margin: 0 };

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
  return (
    <div style={{ display: 'grid', gap: 12, marginTop: 12 }}>
      <fieldset style={FIELDSET_STYLE}>
        <legend style={LEGEND_STYLE}>Backdrop</legend>
        {BACKDROP_OPTIONS.map((option) => (
          <label key={option.value} style={RADIO_LABEL_STYLE}>
            <input
              type="radio"
              name="overlay-editor-backdrop"
              value={option.value}
              checked={backdrop === option.value}
              disabled={option.value === 'captured' && !hasCapturedFrame}
              onChange={() => onBackdropChange(option.value)}
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

  const cameraItems = cameras?.items ?? [];
  const camerasTruncated = cameras !== undefined && !cameras.complete;

  return (
    <div style={{ display: 'grid', gap: 8 }}>
      <FormField label="Camera" htmlFor="overlay-editor-camera">
        <select
          id="overlay-editor-camera"
          style={SELECT_STYLE}
          value={selectedCamera}
          onChange={(e) => onCameraChange(e.target.value)}
        >
          <option value="">{emptyCameraLabel(camerasLoading, camerasFailed, cameraItems.length === 0)}</option>
          {cameraItems.map((camera) => (
            <option key={camera.cameraIdentifier} value={camera.cameraIdentifier}>
              {camera.name}
            </option>
          ))}
        </select>
      </FormField>
      {/* Spec 048's truncation notice, mirrored from LayoutEditorDialog — how
          many of how many, and stops there. */}
      {camerasTruncated && cameras !== undefined && (
        <p style={NOTICE_STYLE}>
          Showing {cameraItems.length} of {cameras.count} cameras.
        </p>
      )}
      <div style={{ display: 'flex', gap: 8 }}>
        <button
          type="button"
          onClick={onCapture}
          disabled={selectedCamera === '' || captureState === 'capturing'}
          style={BUTTON_STYLE}
        >
          Capture frame
        </button>
        {captureState === 'capturing' && (
          <button type="button" onClick={onCancelCapture} style={BUTTON_STYLE}>
            Cancel capture
          </button>
        )}
      </div>
      {captureState === 'failed' && (
        <p role="alert" style={ALERT_STYLE}>
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
