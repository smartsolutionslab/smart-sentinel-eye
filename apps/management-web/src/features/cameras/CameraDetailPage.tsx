import { useGetCameraQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import { useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { EditCameraAddressDialog } from './EditCameraAddressDialog.js';
import { RetireCameraDialog } from './RetireCameraDialog.js';

const RETIRED = 'Decommissioned';

/**
 * One camera (spec 030 FR-001). The endpoint has existed since spec 029 and
 * nothing called it until now.
 */
export function CameraDetailPage() {
  const { cameraIdentifier = '' } = useParams();
  const [editing, setEditing] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const { data: camera, isLoading, error } = useGetCameraQuery({ cameraIdentifier });

  if (isLoading) {
    return <Surface>Loading…</Surface>;
  }

  // FR-008. Every refusal renders the same sentence, and that is the whole
  // requirement rather than an omission.
  //
  // The API answers a camera in another fab exactly as it answers one that was
  // never registered, because a camera record carries its RTSP address and a
  // distinguishable refusal lets an operator enumerate another plant's hardware
  // one request at a time. The app can undo that in a single helpful sentence —
  // "you do not have access to this camera" — which is why there is nothing to
  // branch on here and no error code is inspected.
  if (error !== undefined || camera === undefined) {
    return (
      <Surface>
        <h1 className="text-2xl font-semibold">No such camera</h1>
        <p className="text-fg-muted">
          Nothing here matches that identifier. <Link to="/cameras">Back to cameras</Link>
        </p>
      </Surface>
    );
  }

  const retired = camera.status === RETIRED;

  return (
    <Surface>
      <header className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{camera.name}</h1>
          {retired ? (
            <span className="rounded-md bg-fg-muted/15 px-2 py-1 text-xs font-medium text-fg-muted">
              Retired
            </span>
          ) : null}
        </div>
        <div className="flex items-center gap-4">
          {/* FR-007: absent for a retired camera, not present-and-failing. The
              refusal has to be visible before the attempt — discovering it on
              submit is the thing the requirement rules out. */}
          {retired ? null : <Button onClick={() => setEditing(true)}>Correct the address</Button>}
          {/* Spec 032 FR-004, gated the same way and for the same reason. A
              retired camera cannot be retired again in any sense the operator
              cares about, and offering the control would imply otherwise. */}
          {retired ? null : (
            <Button variant="danger" onClick={() => setRetiring(true)}>
              Retire camera
            </Button>
          )}
          <Link to="/cameras" className="text-sm text-fg-muted hover:text-fg-primary">
            Back to cameras
          </Link>
        </div>
      </header>

      <EditCameraAddressDialog
        open={editing}
        onOpenChange={setEditing}
        cameraIdentifier={camera.cameraIdentifier}
        version={camera.version}
        currentUrl={camera.rtspUrl}
      />

      <RetireCameraDialog
        open={retiring}
        onOpenChange={setRetiring}
        cameraIdentifier={camera.cameraIdentifier}
        name={camera.name}
      />

      {/* FR-007. A retired camera opens and says so — the record outlives the
          hardware, and the audit trail refers to it. What it must not do is
          offer an edit control that fails on submit, which is why the refusal
          is stated here rather than discovered later. */}
      {retired ? (
        <p role="status" className="mb-6 rounded-md border border-fg-muted/30 px-3 py-2 text-sm text-fg-muted">
          This camera is retired. Its record is kept, but it can no longer be changed.
        </p>
      ) : null}

      <dl className="grid grid-cols-[10rem_1fr] gap-y-3 text-sm">
        <Field label="Fab">{camera.fab}</Field>
        <Field label="RTSP URL">
          <code className="text-xs text-fg-muted">{camera.rtspUrl}</code>
        </Field>
        <Field label="Registered">{new Date(camera.registeredAt).toLocaleString()}</Field>
        <Field label="Status">{camera.status}</Field>
      </dl>
    </Surface>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-fg-muted">{label}</dt>
      <dd>{children}</dd>
    </>
  );
}

function Surface({ children }: { children: ReactNode }) {
  return <section className="p-6">{children}</section>;
}
