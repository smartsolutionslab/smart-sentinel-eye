import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

const listCameras = vi.hoisted(() => vi.fn());
const getCamera = vi.hoisted(() =>
  vi.fn(() => ({ data: undefined as unknown, isLoading: false, error: undefined as unknown })),
);

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useGetCameraQuery: (...args: unknown[]) => getCamera(...(args as [])),
    // Not stubbed with data on purpose: T025 asserts this is never called, so a
    // detail view that reached for the catalogue would fail rather than quietly
    // work off a cached list.
    useListCamerasQuery: (...args: unknown[]) => {
      listCameras(...(args as []));
      return { data: undefined, isLoading: false, isFetching: false, error: undefined, refetch: vi.fn() };
    },
  };
});

const ACCESS_TOKEN = 'operator-access-token';
const RENEWED_TOKEN = 'operator-access-token-after-silent-renew';

// Mutable so a test can move the token the way a silent renew does, and see
// whether the getter follows it without changing identity.
const currentToken = { value: ACCESS_TOKEN };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: currentToken.value } }),
}));

// Every render of the viewer, in order, with the props it was handed. An array
// rather than a single capture because T009 compares `getToken` across two
// renders — a stability check needs both, and keeping only the latest would
// pass against a getter rebuilt on every render.
const viewerRenders = vi.hoisted(() => [] as { cameraIdentifier: string; getToken: () => Promise<string | null> }[]);

// CameraViewer mounts a WhepClient that talks to RTCPeerConnection, and jsdom
// has no such global — so the composite is stubbed rather than simulated. This
// is the same reason CameraViewerPanel.test.tsx stubbed it, carried across when
// that file was deleted.
//
// It is also the honest limit of this file: nothing below proves a picture
// appears. What it proves is that the page *reaches* the viewer and hands it a
// credential the operator actually holds.
vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: (props: { cameraIdentifier: string; getToken: () => Promise<string | null> }) => {
    viewerRenders.push(props);
    return <div data-testid="camera-viewer">viewer:{props.cameraIdentifier}</div>;
  },
}));

const { CameraDetailPage } = await import('./CameraDetailPage.js');
const { store } = await import('../../app/store.js');

const camera = {
  cameraIdentifier: '11111111-1111-1111-1111-111111111111',
  version: 7,
  fab: 'munich',
  name: 'Line-1-Entrance',
  rtspUrl: 'rtsp://10.0.5.12/h264',
  registeredAt: '2026-05-24T10:00:00Z',
  status: 'Registered',
};

function renderAt(identifier: string) {
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/cameras/${identifier}`]}>
        <Routes>
          <Route path="/cameras/:cameraIdentifier" element={<CameraDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe('CameraDetailPage', () => {
  beforeEach(() => {
    listCameras.mockClear();
    viewerRenders.length = 0;
    currentToken.value = ACCESS_TOKEN;
    getCamera.mockReturnValue({ data: camera, isLoading: false, error: undefined });
  });

  it('Shows the camera it was asked for', () => {
    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByText('munich')).toBeInTheDocument();
    expect(screen.getByText('rtsp://10.0.5.12/h264')).toBeInTheDocument();

    // The counterpart to the retired case below: an active camera offers the
    // correction, so the absence there is scoping rather than the control
    // simply never having been built.
    expect(screen.getByRole('button', { name: /correct the address/i })).toBeInTheDocument();
  });

  /**
   * FR-003 and SC-002. A detail view that rendered correctly while fetching the
   * whole catalogue would pass every other test in this file — the saving is
   * invisible from the outside, so it has to be asserted directly.
   */
  it('Does not fetch the catalogue to show one camera', () => {
    renderAt(camera.cameraIdentifier);

    expect(listCameras).not.toHaveBeenCalled();
  });

  /**
   * FR-007. Retirement takes a camera out of the default listing, not out of
   * existence — the record is what the audit trail refers to.
   */
  it('Opens a retired camera and says it is retired', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();

    // The explanation, not just the badge — an operator has to know the record
    // is kept and that the camera cannot be changed, which is what stops the
    // absence of an edit control reading as a bug.
    expect(screen.getByRole('status')).toHaveTextContent(/retired/i);
    expect(screen.getByRole('status')).toHaveTextContent(/no longer be changed/i);

    // T031. Absent, not present-and-failing. Asserting that submitting fails
    // would pass against a dialog the operator can open and fill in before
    // being refused, which is exactly what FR-007 rules out.
    expect(screen.queryByRole('button', { name: /correct the address/i })).toBeNull();
  });

  /**
   * Spec 032 T013 / FR-004. The same shape as the assertion above, for the
   * same reason: a disabled control still tells the operator the action is
   * conceptually available, and for a terminal state that is untrue.
   */
  it('Offers no way to retire a camera that is already retired', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    renderAt(camera.cameraIdentifier);

    expect(screen.queryByRole('button', { name: /retire camera/i })).toBeNull();
  });

  /**
   * The counterpart, without which the assertion above passes against a page
   * that renders no controls at all — including on an active camera, where the
   * whole feature would then be missing and every test still green.
   */
  it('Offers to retire an active camera', () => {
    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('button', { name: /retire camera/i })).toBeInTheDocument();
  });

  /**
   * Spec 035 T014 / FR-009. A third control, gated the same way and asserted
   * the same way — absent, not disabled. A disabled control says the action is
   * conceptually available, and for a terminal state that is untrue.
   */
  it('Offers no way to rename a camera that is already retired', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    renderAt(camera.cameraIdentifier);

    expect(screen.queryByRole('button', { name: /^rename$/i })).toBeNull();
  });

  /** The counterpart, so the absence above cannot pass vacuously. */
  it('Offers to rename an active camera', () => {
    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('button', { name: /^rename$/i })).toBeInTheDocument();
  });

  /**
   * FR-001 / FR-006 — asserted **from the page**, which is the whole point.
   *
   * `CameraViewerPanel` had three green tests describing a viewer no operator
   * could reach, because they rendered the component directly. That state was
   * indistinguishable from a working one for four specs. Rendering the composite
   * in isolation does not count as reaching it; opening the camera's page does.
   */
  it('Shows the camera it was asked for, live', () => {
    renderAt(camera.cameraIdentifier);

    expect(screen.getByTestId('camera-viewer').textContent).toContain(camera.cameraIdentifier);
  });

  /**
   * FR-003 / FR-007, and the assertion the previous code failed.
   *
   * The deleted placeholder read `sessionStorage.getItem('keycloak:access_token')`
   * — a key nothing in the product writes — so it rendered perfectly and resolved
   * to `null`. A viewer holding a credential nobody issued fails exactly like no
   * viewer at all, and passes any check that asks only whether something rendered.
   *
   * The second half guards issue 1889: a `getToken` rebuilt on every render
   * clears `CameraViewer`'s decode sampler before it takes a second sample, so
   * the leg reports none. Nothing visible fails.
   *
   * The token is moved the way a silent renew moves it, because a re-render
   * alone is too weak a test. Opening a dialog only changes local state, so
   * `useCallback(…, [auth.user?.access_token])` would hold its identity across
   * one and pass — while restarting the sampler on every renew in production.
   * Changing the token separates empty deps from that, and asserts the ref
   * still carries the new value: stability must not cost freshness.
   */
  it('Hands the viewer the operator token, from a getter that survives a silent renew', async () => {
    const user = userEvent.setup();
    renderAt(camera.cameraIdentifier);

    const first = viewerRenders.at(0);
    expect(first).toBeDefined();
    await expect(first!.getToken()).resolves.toBe(ACCESS_TOKEN);

    // A plain re-render first — the weaker half, which catches an inline arrow.
    await user.click(screen.getByRole('button', { name: /^rename$/i }));
    expect(viewerRenders.length).toBeGreaterThan(1);
    expect(viewerRenders.at(-1)!.getToken).toBe(first!.getToken);

    // Then the renew. Same function, new token.
    currentToken.value = RENEWED_TOKEN;
    await user.click(screen.getByRole('button', { name: /^cancel$/i }));

    expect(viewerRenders.at(-1)!.getToken).toBe(first!.getToken);
    await expect(first!.getToken()).resolves.toBe(RENEWED_TOKEN);
  });

  /**
   * FR-004, both halves in one test because one without the other is the
   * ambiguity the requirement removes.
   *
   * Retirement stops the stream deliberately. A page that simply omitted the
   * viewer would leave a reader free to conclude the video is broken — and a
   * viewer left mounted would report "Stream is offline", describing an intended
   * outcome as a fault.
   */
  it('Shows a retired camera no viewer, and says the stream is why', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    renderAt(camera.cameraIdentifier);

    expect(screen.queryByTestId('camera-viewer')).toBeNull();
    expect(screen.getByRole('status')).toHaveTextContent(/stream/i);
  });

  /**
   * Spec 032 T014 / FR-012 — asserted as an **absence**, which is why it needs
   * this explanation to survive review.
   *
   * Retirement is idempotent: the endpoint answers `204` whether or not this
   * operator caused it. So the app cannot distinguish "I retired it" from "it
   * was already retired", and must not narrate one. Open the same camera in
   * two tabs and retire in both — both succeed, and a page announcing "Camera
   * retired" has told one of them something false.
   *
   * <p>
   * The retired notice already on the page is fine and is asserted above: "This
   * camera **is** retired" describes a state. What is forbidden is reporting an
   * **event** — a claim about what just happened and who caused it. There is no
   * toast infrastructure in this app and none is added; the page state is the
   * feedback.
   * </p>
   */
  it('Never claims this operator retired the camera', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    const { container } = renderAt(camera.cameraIdentifier);

    expect(container.textContent).not.toMatch(/camera retired/i);
    expect(container.textContent).not.toMatch(/you retired/i);
    expect(container.textContent).not.toMatch(/has been retired/i);
    expect(container.textContent).not.toMatch(/successfully/i);
  });

  /**
   * FR-008, and the reason this file asserts sameness rather than a message.
   *
   * The API answers a camera in another fab exactly as it answers one that
   * never existed, because a camera record carries its RTSP address. The app
   * can undo that in one helpful sentence, so what is checked is that both
   * causes render the *same* thing — not that each renders something.
   */
  it('Reports a camera it may not see exactly as one that does not exist', () => {
    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });
    const { container: refusedForAnotherFab } = renderAt('22222222-2222-2222-2222-222222222222');
    const crossFab = refusedForAnotherFab.innerHTML;

    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });
    const { container: refusedForUnknown } = renderAt('33333333-3333-3333-3333-333333333333');

    expect(refusedForUnknown.innerHTML).toBe(crossFab);
    expect(screen.getAllByRole('heading', { name: /no such camera/i }).length).toBeGreaterThan(0);

    // Spec 032 T015. Re-checked because this feature added a control that could
    // appear for one cause and not the other: "no retire button, because this
    // isn't yours" and "no retire button, because there is no camera" have to
    // be the same page. The innerHTML comparison above already covers it, but
    // only implicitly — and an implicit guarantee is one a later refactor can
    // drop without any test naming what was lost.
    expect(screen.queryByRole('button', { name: /retire camera/i })).toBeNull();

    // Spec 035 T015. The third control, named for the same reason. Each feature
    // that adds one adds a way for the two causes to look different, so each
    // names its own rather than trusting the comparison above to have covered
    // something nobody wrote down.
    expect(screen.queryByRole('button', { name: /^rename$/i })).toBeNull();
  });

  it('Says nothing about access, ever', () => {
    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });

    const { container } = renderAt('22222222-2222-2222-2222-222222222222');

    // The specific sentence that would undo spec 029's indistinguishability at
    // the last hop, and the one most likely to be added later as an
    // improvement.
    expect(container.textContent).not.toMatch(/access|permission|not yours|another fab/i);
  });
});
