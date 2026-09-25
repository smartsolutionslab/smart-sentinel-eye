import { useGetCameraQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useCallback, useRef, useState, type ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { Link, useParams } from 'react-router-dom';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { EditCameraAddressDialog } from './EditCameraAddressDialog.js';
import { RenameCameraDialog } from './RenameCameraDialog.js';
import { RetireCameraDialog } from './RetireCameraDialog.js';

const RETIRED = 'Decommissioned';

/**
 * One camera (spec 030 FR-001). The endpoint has existed since spec 029 and
 * nothing called it until now.
 */
export function CameraDetailPage() {
  const { cameraIdentifier = '' } = useParams();
  const auth = useAuth();
  // Stable identity, holding the newest token behind a ref. `CameraViewer` puts
  // this into the dependency array of its decode sampler, so a fresh function
  // each render clears that interval before it can take a second sample and the
  // leg reports zero of them (issue 1889). `useWhepSession` guards its own use
  // of the prop behind a ref and says why, so the peer connection survives an
  // unstable getter — the sampler is what does not.
  //
  // Empty deps, not [auth.user?.access_token]: the ref already carries the
  // newest token, so keying on its value would restart the sampler on every
  // silent renew for no gain. Nothing visible fails either way, which is why
  // the page test asserts the identity across a token change rather than
  // trusting this comment.
  const accessTokenRef = useRef(auth.user?.access_token);
  // Deliberate, for the reason above; the page test asserts the identity holds
  // across a token change, so the behaviour is pinned by a test rather than
  // merely claimed in a comment.
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth.user?.access_token;
  const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
  const [editing, setEditing] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const {
    data: camera,
    currentData,
    isLoading,
    isFetching,
    error,
    refetch,
  } = useGetCameraQuery({ cameraIdentifier });

  // `camera` (`data`) can still hold a *previously viewed* identifier's record
  // for a moment after the URL changes — including across a same-instance
  // navigation away and back, where RTK Query's `lastResult` fallback carries
  // it forward with no refetch in between — while `currentData` is only ever
  // the cache entry for the identifier being requested right now. `record` is
  // what both the gate below and every render site read, so neither can
  // disagree about which identifier is on screen.
  const record = error !== undefined ? currentData : camera;

  // `data` may still be the previous identifier's record (see above); while
  // this identifier's first response is in flight and nothing has failed,
  // show loading. A background refetch keeps `currentData`, and a refusal
  // keeps `error`, so neither reaches this branch.
  if (isLoading || (isFetching && currentData === undefined && error === undefined)) {
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
  //
  // The second clause guards a navigation, not a refresh: `record` resolves to
  // `currentData` whenever `error` is set, and `currentData` is only ever the
  // cache entry for the identifier being requested right now. Refusing whenever
  // a failed request has no record for the current identifier — rather than
  // whenever it merely has no record at all — is what keeps a refused camera in
  // another fab (or a stale record carried over from one) from rendering as the
  // last camera the operator happened to be looking at.
  if (record === undefined) {
    return (
      <Surface>
        <h1 className="text-2xl font-semibold">No such camera</h1>
        <p className="text-fg-muted">
          Nothing here matches that identifier. <Link to="/cameras">Back to cameras</Link>
        </p>
      </Surface>
    );
  }

  const retired = record.status === RETIRED;

  return (
    <Surface>
      <header className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{record.name}</h1>
          {retired ? (
            <span className="rounded-md bg-fg-muted/15 px-2 py-1 text-xs font-medium text-fg-muted">Retired</span>
          ) : null}
        </div>
        <div className="flex items-center gap-4">
          {/* Spec 035 FR-009, gated like the two beside it. Ordered first
              because it corrects the thing the heading shows; the destructive
              control stays last, before the link. A fourth control here wants a
              menu rather than a fourth button. */}
          {retired ? null : (
            <Button variant="secondary" onClick={() => setRenaming(true)}>
              Rename
            </Button>
          )}
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

      {error !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          Could not refresh this camera — what you see may be out of date.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      {/* FR-001/FR-002: the picture is part of the page, not something opened
          and dismissed. CameraViewer already carries `relative aspect-video
          w-full`, so the page supplies a maximum width and nothing else — an
          aspect or height constraint here would fight it.

          FR-004: absent for a retired camera, gated like every other control
          above. Retirement stops the stream deliberately, so a viewer reporting
          "Stream is offline" would describe an intended outcome as a fault; the
          notice below says so in words instead. */}
      {retired ? null : (
        <div className="mb-6 max-w-3xl">
          <CameraViewer cameraIdentifier={record.cameraIdentifier} getToken={getToken} cameraName={record.name} />
        </div>
      )}

      <EditCameraAddressDialog
        open={editing}
        onOpenChange={setEditing}
        cameraIdentifier={record.cameraIdentifier}
        version={record.version}
        currentUrl={record.rtspUrl}
      />

      <RenameCameraDialog
        open={renaming}
        onOpenChange={setRenaming}
        cameraIdentifier={record.cameraIdentifier}
        version={record.version}
        currentName={record.name}
      />

      <RetireCameraDialog
        open={retiring}
        onOpenChange={setRetiring}
        cameraIdentifier={record.cameraIdentifier}
        name={record.name}
      />

      {/* FR-007. A retired camera opens and says so — the record outlives the
          hardware, and the audit trail refers to it. What it must not do is
          offer an edit control that fails on submit, which is why the refusal
          is stated here rather than discovered later. */}
      {retired ? (
        <p role="status" className="mb-6 rounded-md border border-fg-muted/30 px-3 py-2 text-sm text-fg-muted">
          This camera is retired. Its record is kept, but it can no longer be changed, and its live stream has stopped —
          there is no picture to show.
        </p>
      ) : null}

      <dl className="grid grid-cols-[10rem_1fr] gap-y-3 text-sm">
        <Field label="Fab">{record.fab}</Field>
        <Field label="RTSP URL">
          <code className="text-xs text-fg-muted">{record.rtspUrl}</code>
        </Field>
        <Field label="Registered">{new Date(record.registeredAt).toLocaleString()}</Field>
        <Field label="Status">{record.status}</Field>
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
