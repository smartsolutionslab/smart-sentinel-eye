import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store.js';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

/** Spec 441 (issue #3072) review fixes on the form: FR-003 geometry and a persistent overlap status. */

const createDraftMock = vi.fn(async (_body: unknown) => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
    useGetOverlayQuery: () => ({ data: undefined, currentData: undefined, isLoading: false, isError: false }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }) };
});

const { OverlayDraftForm } = await import('./OverlayDraftForm.js');

let store: ReturnType<typeof createApiStore>;

beforeEach(() => {
  store = createApiStore(apiSlices);
  createDraftMock.mockClear();
});

afterEach(cleanup);

/** Spec 457: the single "Add label" button became an "Add" menu; choosing Text is the same act. */
async function addLabel(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Add' }));
  await user.click(await screen.findByRole('menuitem', { name: 'Text' }));
}

function renderForm() {
  return render(
    <Provider store={store}>
      <OverlayDraftForm onDone={() => undefined} onCancel={() => undefined} />
    </Provider>,
  );
}

describe('OverlayDraftForm review fixes', () => {
  it('Starts an added label on the create-default geometry, overlapping the first (FR-003)', async () => {
    const user = userEvent.setup();
    renderForm();
    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');
    await addLabel(user);
    expect(screen.getAllByTestId('overlay-editor-overlap-warning')).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    const [body] = createDraftMock.mock.calls[0] as unknown as [{ elements: Array<Record<string, unknown>> }];
    const [first, second] = body.elements;
    expect(second).toMatchObject({
      normalizedX: first!['normalizedX'],
      normalizedY: first!['normalizedY'],
      normalizedWidth: first!['normalizedWidth'],
      normalizedHeight: first!['normalizedHeight'],
    });
  });

  it('Keeps one overlap status container mounted whether or not labels overlap', async () => {
    const user = userEvent.setup();
    renderForm();
    const container = screen.getByTestId('overlay-editor-overlap-status');
    expect(container).toHaveAttribute('role', 'status');
    expect(screen.queryByTestId('overlay-editor-overlap-warning')).not.toBeInTheDocument();

    await addLabel(user);
    expect(screen.getByTestId('overlay-editor-overlap-status')).toBe(container);
    expect(container).toContainElement(screen.getByTestId('overlay-editor-overlap-warning'));
  });
});
