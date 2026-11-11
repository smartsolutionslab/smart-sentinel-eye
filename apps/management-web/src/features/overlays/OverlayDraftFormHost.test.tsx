import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditTarget } from './OverlayDraftForm';

/**
 * Characterisation of `OverlayDraftForm` behaviour that no other suite pinned
 * (spec 477, T003): the form's contract with its host and with the RTK Query
 * hooks it reads, observed through the real editor and layer list. Written
 * against the unsplit form; it must stay green, unmodified, across the split.
 */

const createDraftMock = vi.fn(async (_body: unknown) => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));
const createReset = vi.fn();
const editReset = vi.fn();
let createState: { isLoading: boolean; error: unknown } = { isLoading: false, error: undefined };
let editState: { isLoading: boolean; error: unknown } = { isLoading: false, error: undefined };

interface ChainState {
  data?: { overlayIdentifier: string; version: number };
  currentData?: { overlayIdentifier: string; version: number };
  isError: boolean;
  isFetching: boolean;
}
let chainState: ChainState = { isError: false, isFetching: false };
const useGetOverlayQueryMock = vi.fn((..._args: unknown[]) => ({
  ...chainState,
  isLoading: false,
  refetch: vi.fn(),
}));

interface ResolveState {
  data?: unknown;
  currentData?: unknown;
  isFetching: boolean;
  isError: boolean;
}
let resolveState: ResolveState = { isFetching: false, isError: false };

let cameraState: { data: unknown; isLoading: boolean; isError: boolean } = {
  data: { items: [], count: 0, complete: true },
  isLoading: false,
  isError: false,
};

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { ...createState, reset: createReset }],
    useEditDraftOverlayRevisionMutation: () => [editDraftMock, { ...editState, reset: editReset }],
    useGetOverlayQuery: (...args: unknown[]) => useGetOverlayQueryMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return { ...actual, useResolveOverlayTextQuery: () => resolveState };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return { ...actual, useListAllCameraChoicesQuery: () => ({ ...cameraState, isFetching: false }) };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }) };
});

const { OverlayDraftForm } = await import('./OverlayDraftForm');

const OVERLAY_IDENTIFIER = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const CHAIN = { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 };

let store: ReturnType<typeof createApiStore>;

beforeEach(() => {
  store = createApiStore(apiSlices);
  createDraftMock.mockClear();
  editDraftMock.mockClear();
  createReset.mockClear();
  editReset.mockClear();
  useGetOverlayQueryMock.mockClear();
  createState = { isLoading: false, error: undefined };
  editState = { isLoading: false, error: undefined };
  chainState = { data: CHAIN, currentData: CHAIN, isError: false, isFetching: false };
  resolveState = { isFetching: false, isError: false };
  cameraState = { data: { items: [], count: 0, complete: true }, isLoading: false, isError: false };
});

afterEach(cleanup);

function text(value: string, y = 0.1): OverlayElement {
  return {
    kind: 'Text',
    color: '#9F9F9FD9',
    text: value,
    normalizedX: 0.1,
    normalizedY: y,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  };
}

function target(elements: OverlayElement[]): OverlayEditTarget {
  return { overlayIdentifier: OVERLAY_IDENTIFIER, revisionNumber: 1, name: 'Line 1 readings', elements };
}

type FormProps = Partial<Parameters<typeof OverlayDraftForm>[0]>;

function tree(props: FormProps) {
  return (
    <Provider store={store}>
      <OverlayDraftForm onDone={() => undefined} onCancel={() => undefined} {...props} />
    </Provider>
  );
}

function renderForm(props: FormProps = {}) {
  return render(tree(props));
}

function layerChip(name: string): HTMLElement {
  return within(screen.getByRole('list', { name: 'Layers' })).getByRole('button', { name });
}

async function replaceLabelText(user: ReturnType<typeof userEvent.setup>, value: string) {
  const field = screen.getByTestId('overlay-editor-text');
  await user.clear(field);
  await user.type(field, value);
}

describe('OverlayDraftForm host contract - chain read', () => {
  it('Re-reads the chain from the server on every mount instead of answering from cache', () => {
    renderForm({ editTarget: target([text('Line 1')]) });

    expect(useGetOverlayQueryMock).toHaveBeenCalledWith(
      OVERLAY_IDENTIFIER,
      expect.objectContaining({ refetchOnMountOrArgChange: true }),
    );
  });

  it('Keeps Save closed and unsubmittable while the retained result is stale and the read is in flight', async () => {
    const user = userEvent.setup();
    chainState = { data: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 3 }, isError: false, isFetching: true };
    renderForm({ editTarget: target([text('Line 1')]) });

    const save = screen.getByRole('button', { name: /^save draft$/i });
    expect(save).toHaveAttribute('aria-disabled', 'true');
    await user.click(save);
    expect(editDraftMock).not.toHaveBeenCalled();
  });

  it('Prevents the native submit while Save is blocked', () => {
    chainState = { data: CHAIN, currentData: CHAIN, isError: false, isFetching: true };
    const { container } = renderForm({ editTarget: target([text('Line 1')]) });

    const notPrevented = fireEvent.submit(container.querySelector('form')!);

    expect(notPrevented).toBe(false);
    expect(editDraftMock).not.toHaveBeenCalled();
  });
});

describe('OverlayDraftForm host contract - unmount', () => {
  it('Resets both mutation states when the form unmounts, in edit mode', () => {
    const { unmount } = renderForm({ editTarget: target([text('Line 1')]) });
    expect(createReset).not.toHaveBeenCalled();
    expect(editReset).not.toHaveBeenCalled();

    unmount();

    expect(createReset).toHaveBeenCalledTimes(1);
    expect(editReset).toHaveBeenCalledTimes(1);
  });

  it('Resets both mutation states when the form unmounts, in create mode', () => {
    const { unmount } = renderForm();

    unmount();

    expect(createReset).toHaveBeenCalledTimes(1);
    expect(editReset).toHaveBeenCalledTimes(1);
  });
});

describe('OverlayDraftForm host contract - seeding and saving', () => {
  it('Re-seeds the form when the edit target changes under a mounted form', async () => {
    const { rerender } = renderForm({ editTarget: target([text('Alpha')]) });
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Alpha');

    rerender(tree({ editTarget: target([text('Beta')]) }));

    await waitFor(() => expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Beta'));
  });

  it('Calls onDone once a create is saved', async () => {
    const user = userEvent.setup();
    const onDone = vi.fn();
    renderForm({ onDone });

    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
  });

  it('Does not call onDone when the create is refused', async () => {
    const user = userEvent.setup();
    const onDone = vi.fn();
    createDraftMock.mockResolvedValueOnce({ error: { status: 409 } } as never);
    renderForm({ onDone });

    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    await waitFor(() => expect(createDraftMock).toHaveBeenCalledTimes(1));
    expect(onDone).not.toHaveBeenCalled();
  });

  it('Clears the create form after a successful save', async () => {
    const user = userEvent.setup();
    const onDone = vi.fn();
    renderForm({ onDone });

    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');
    await replaceLabelText(user, 'Typed text');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByLabelText(/name/i)).toHaveValue(''));
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Overlay text');
  });

  it('Restores the seeded values after a successful edit', async () => {
    const user = userEvent.setup();
    const onDone = vi.fn();
    renderForm({ editTarget: target([text('Seeded')]), onDone });

    await replaceLabelText(user, 'Changed');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Seeded'));
  });
});

describe('OverlayDraftForm host contract - host callback', () => {
  it('Tells the newest onElementsChange of the live elements, not the one given at mount', async () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderForm({ editTarget: target([text('Alpha')]), onElementsChange: first });
    rerender(tree({ editTarget: target([text('Alpha')]), onElementsChange: second }));
    first.mockClear();
    second.mockClear();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: 'Edited' } });

    await waitFor(() => expect(second).toHaveBeenCalled());
    const told = second.mock.calls[second.mock.calls.length - 1]![0] as Array<{ text?: string }>;
    expect(told[0]!.text).toBe('Edited');
    expect(first).not.toHaveBeenCalled();
  });
});

describe('OverlayDraftForm host contract - layer selection after a removal', () => {
  it('Selects the next layer in the list when the selected front-most layer is removed', async () => {
    const user = userEvent.setup();
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.3), text('C', 0.5)]) });

    await user.click(layerChip('C'));
    expect(layerChip('C')).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByRole('button', { name: 'Remove C' }));

    expect(layerChip('B')).toHaveAttribute('aria-pressed', 'true');
    expect(layerChip('A')).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('B');
  });
});

describe('OverlayDraftForm host contract - resolve preview', () => {
  it('Says it is resolving while the typed text has not yet settled', async () => {
    renderForm();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{temperature}}' } });

    const panel = screen.getByRole('region', { name: 'Placeholder preview' });
    expect(panel).toHaveTextContent('Resolving…');
    await waitFor(() => expect(panel).not.toHaveTextContent('Resolving…'));
  });

  it('Never shows a retained earlier resolve for text whose own resolve was refused', async () => {
    resolveState = {
      data: {
        resolvedText: '23.4',
        placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
      },
      currentData: undefined,
      isFetching: false,
      isError: true,
    };
    renderForm();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{pressure}}' } });

    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Placeholder preview' })).toHaveTextContent(
        'Could not check this text right now.',
      ),
    );
    expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('{{pressure}}');
    expect(screen.queryByTestId('placeholder-advisory-resolved')).toBeNull();
  });
});

describe('OverlayDraftForm host contract - camera list and refusals', () => {
  it('Tells the editor the camera list could not be loaded', () => {
    cameraState = { data: undefined, isLoading: false, isError: true };
    renderForm();

    expect(screen.getByRole('option', { name: 'Cameras could not be loaded' })).toBeInTheDocument();
  });

  it('Says a revision that left draft is no longer a draft when the refusal carries no detail', () => {
    editState = {
      isLoading: false,
      error: { status: 409, data: { title: 'OVERLAY_REVISION_NOT_DRAFT' } },
    };
    renderForm({ editTarget: target([text('Line 1')]) });

    expect(
      screen.getByText('This revision is no longer a draft. Reload to see its current state.'),
    ).toBeInTheDocument();
  });
});

describe('OverlayDraftForm host contract - readOnly', () => {
  it('Ignores typing into the label while readOnly', async () => {
    const user = userEvent.setup();
    renderForm({ editTarget: target([text('Seeded')]), readOnly: true });

    await user.type(screen.getByTestId('overlay-editor-text'), 'xyz');

    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Seeded');
  });

  it('Marks the editor and the layer list busy while readOnly, and neither otherwise', () => {
    const { rerender } = renderForm({ editTarget: target([text('Seeded')]), readOnly: true });

    expect(screen.getByTestId('overlay-editor-text').closest('[aria-busy="true"]')).not.toBeNull();
    expect(screen.getByRole('list', { name: 'Layers' }).closest('[aria-busy="true"]')).not.toBeNull();

    rerender(tree({ editTarget: target([text('Seeded')]), readOnly: false }));

    expect(screen.getByTestId('overlay-editor-text').closest('[aria-busy]')).toBeNull();
    expect(screen.getByRole('list', { name: 'Layers' }).closest('[aria-busy]')).toBeNull();
  });

  it('Makes the layer list controls unavailable while readOnly even though there are several layers', () => {
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]), readOnly: true });

    expect(screen.getByRole('button', { name: 'Remove A' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Move B down' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Add' })).toHaveAttribute('aria-disabled', 'true');
  });
});
