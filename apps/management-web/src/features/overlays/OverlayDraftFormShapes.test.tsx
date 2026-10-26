import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store.js';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditTarget } from './OverlayDraftForm.js';

/**
 * Spec 457 (issue #3107) C6. New behaviour, RED (ADR-0139): the form adds, selects and edits
 * a Box or an Ellipse. Today a selected shape shows a notice and the layer list can only add
 * a label, so every case below fails on a missing "Add" menu or a missing shape inspector.
 * The mocks mirror `OverlayDraftFormLayers.test.tsx`.
 */

const createDraftMock = vi.fn(async (_body: unknown) => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown): Promise<unknown> => ({ data: 1 }));
const cameraChoicesMock = vi.fn();
let editError: unknown = undefined;
let chainQueryState: { data?: { overlayIdentifier: string; version: number } } = { data: undefined };

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [
      editDraftMock,
      { isLoading: false, error: editError, reset: vi.fn(() => (editError = undefined)) },
    ],
    useGetOverlayQuery: () => ({
      data: chainQueryState.data,
      currentData: chainQueryState.data,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return { ...actual, useListAllCameraChoicesQuery: (...args: unknown[]) => cameraChoicesMock(...args) };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }) };
});

const { OverlayDraftForm } = await import('./OverlayDraftForm.js');

const OVERLAY_IDENTIFIER = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

let store: ReturnType<typeof createApiStore>;

beforeEach(() => {
  store = createApiStore(apiSlices);
  createDraftMock.mockClear();
  editDraftMock.mockReset();
  editDraftMock.mockResolvedValue({ data: 1 });
  cameraChoicesMock.mockReset();
  cameraChoicesMock.mockReturnValue({
    data: { items: [], count: 0, complete: true },
    isLoading: false,
    isFetching: false,
    isError: false,
  });
  editError = undefined;
  chainQueryState = { data: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 } };
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
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function box(): OverlayElement {
  return {
    kind: 'Box',
    color: '#D32F2F80',
    normalizedX: 0.05,
    normalizedY: 0.05,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
  };
}

function ellipse(): OverlayElement {
  return {
    kind: 'Ellipse',
    color: '#00A0FFCC',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.3,
  };
}

function target(elements: OverlayElement[]): OverlayEditTarget {
  return { overlayIdentifier: OVERLAY_IDENTIFIER, revisionNumber: 1, name: 'Line 1 readings', elements };
}

function renderForm(editTarget?: OverlayEditTarget) {
  return render(
    <Provider store={store}>
      <OverlayDraftForm editTarget={editTarget} onDone={() => undefined} onCancel={() => undefined} />
    </Provider>,
  );
}

function layerList(): HTMLElement {
  return screen.getByRole('list', { name: 'Layers' });
}

function layerNames(): string[] {
  return within(layerList())
    .getAllByRole('listitem')
    .map((item) => within(item).getAllByRole('button')[0]!.textContent ?? '');
}

function layerChip(name: string): HTMLElement {
  return within(layerList()).getByRole('button', { name });
}

async function addFromMenu(user: ReturnType<typeof userEvent.setup>, kind: 'Text' | 'Box' | 'Ellipse') {
  await user.click(screen.getByRole('button', { name: 'Add' }));
  const menu = await screen.findByRole('menu');
  await user.click(within(menu).getByRole('menuitem', { name: kind }));
}

async function commitField(user: ReturnType<typeof userEvent.setup>, label: string, percent: string) {
  const input = screen.getByLabelText(label, { exact: true });
  await user.clear(input);
  await user.type(input, percent);
  await user.tab();
}

function lastBody(mock: typeof editDraftMock | typeof createDraftMock): {
  elements: Array<Record<string, unknown>>;
  version?: number;
} {
  const calls = mock.mock.calls as unknown as ReadonlyArray<
    [{ elements: Array<Record<string, unknown>>; version?: number }]
  >;
  return calls[calls.length - 1]![0];
}

describe('OverlayDraftForm shapes - create', () => {
  it('Adds a Box at the front, selects and focuses it, announces it, and shows a shape inspector', async () => {
    const user = userEvent.setup();
    renderForm();

    await addFromMenu(user, 'Box');

    expect(layerNames()).toEqual(['Box 1', 'Overlay text']);
    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'true');
    expect(document.activeElement).toBe(layerChip('Box 1'));
    expect(screen.getByTestId('overlay-layer-list-live-region')).toHaveTextContent('Box added, position 1 of 2');
    expect(screen.getByLabelText('Left', { exact: true })).toBeInTheDocument();
    expect(screen.getByLabelText('Width', { exact: true })).toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-text')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-font-size')).not.toBeInTheDocument();
  });

  it('Saves an added Box moved to Left 40 % as exactly one POST with the Text first and the Box second', async () => {
    const user = userEvent.setup();
    renderForm();
    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');

    await addFromMenu(user, 'Box');
    await commitField(user, 'Left', '40');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const { elements } = lastBody(createDraftMock);
    expect(elements).toEqual([
      {
        kind: 'Text',
        color: '#9F9F9FD9',
        text: 'Overlay text',
        normalizedX: 0.1,
        normalizedY: 0.1,
        normalizedWidth: 0.3,
        normalizedHeight: 0.08,
        fontSizePx: 32,
      },
      {
        kind: 'Box',
        color: '#9F9F9FD9',
        normalizedX: 0.4,
        normalizedY: 0.1,
        normalizedWidth: 0.3,
        normalizedHeight: 0.3,
      },
    ]);
  });

  it('Saves an added Ellipse with its kind, default colour and default geometry', async () => {
    const user = userEvent.setup();
    renderForm();
    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');

    await addFromMenu(user, 'Ellipse');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(lastBody(createDraftMock).elements[1]).toEqual({
      kind: 'Ellipse',
      color: '#9F9F9FD9',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.3,
    });
  });

  it('Still adds a label from the menu, selecting it with the label editor', async () => {
    const user = userEvent.setup();
    renderForm();

    await addFromMenu(user, 'Text');

    expect(layerNames()).toEqual(['Overlay text', 'Overlay text']);
    expect(screen.getByTestId('overlay-editor-text')).toBeInTheDocument();
  });

  it('Makes Add unavailable at 8 elements of mixed kinds, and a click opens no menu', async () => {
    const user = userEvent.setup();
    renderForm();
    await addFromMenu(user, 'Box');
    await addFromMenu(user, 'Ellipse');
    await addFromMenu(user, 'Box');
    await addFromMenu(user, 'Ellipse');
    await addFromMenu(user, 'Text');
    await addFromMenu(user, 'Box');
    await addFromMenu(user, 'Ellipse');
    expect(within(layerList()).getAllByRole('listitem')).toHaveLength(8);

    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Up to 8 elements')).toBeInTheDocument();
    await user.click(add);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(within(layerList()).getAllByRole('listitem')).toHaveLength(8);
  });
});

describe('OverlayDraftForm shapes - edit', () => {
  it('Selects a Box with a geometry inspector and no label text field, and saves it unchanged', async () => {
    const user = userEvent.setup();
    renderForm(target([box(), text('Zone A')]));

    await user.click(layerChip('Box 1'));
    expect(screen.getByLabelText('Left', { exact: true })).toHaveValue('5');
    expect(screen.queryByTestId('overlay-editor-text')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-shape-selected-notice')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    expect(lastBody(editDraftMock).elements).toEqual([box(), text('Zone A')]);
  });

  it('Keeps an Ellipse kind and colour when it is nudged on the canvas, leaving its sibling untouched', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), ellipse()]));

    await user.click(layerChip('Ellipse 1'));
    fireEvent.keyDown(screen.getByTestId('overlay-editor-label'), { key: 'ArrowRight' });
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(editDraftMock).toHaveBeenCalledTimes(1);
    const body = lastBody(editDraftMock);
    expect(body.version).toBe(7);
    expect(body.elements[0]).toEqual(text('A'));
    expect(body.elements[1]).toMatchObject({ kind: 'Ellipse', color: '#00A0FFCC', normalizedY: 0.5 });
    expect(body.elements[1]!['normalizedX']).toBeCloseTo(0.505, 10);
  });

  it('Keeps a Box kind and colour when a geometry field is committed', async () => {
    const user = userEvent.setup();
    renderForm(target([box(), text('Zone A')]));

    await user.click(layerChip('Box 1'));
    await commitField(user, 'Left', '40');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    const { elements } = lastBody(editDraftMock);
    expect(elements[0]).toEqual({ ...box(), normalizedX: 0.4 });
    expect(elements[1]).toEqual(text('Zone A'));
  });

  it('Opens a shapes-only draft editable: first element selected, geometry shown, no notice', () => {
    renderForm(target([box()]));

    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Left', { exact: true })).toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-dialog-shapes-only-notice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-shape-selected-notice')).not.toBeInTheDocument();
  });

  it('Offers the backdrop camera picker for a shapes-only draft and never skips the camera query', () => {
    renderForm(target([box()]));

    expect(screen.getByRole('combobox', { name: /camera/i })).toBeInTheDocument();
    expect(cameraChoicesMock).toHaveBeenCalled();
    expect(cameraChoicesMock.mock.calls.every((call) => call[1]?.skip !== true)).toBe(true);
  });

  it('Moves a shape down in the list and keeps it selected and editable', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), box()]));

    await user.click(layerChip('Box 1'));
    await user.click(screen.getByRole('button', { name: 'Move Box 1 down' }));

    expect(layerNames()).toEqual(['A', 'Box 1']);
    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Left', { exact: true })).toBeInTheDocument();
  });
});

describe('OverlayDraftForm shapes - refused input and failures', () => {
  it('Refuses a zero Width in its field, never commits it, and saves the Box at its old width', async () => {
    const user = userEvent.setup();
    renderForm(target([box(), text('Zone A')]));

    await user.click(layerChip('Box 1'));
    await commitField(user, 'Width', '0');

    expect(
      screen.getAllByRole('alert').some((alert) => /Width must be greater than 0%/.test(alert.textContent ?? '')),
    ).toBe(true);
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(lastBody(editDraftMock).elements[0]!['normalizedWidth']).toBe(0.2);
  });

  it('Offers Reload, never Try again, when a save with an added Ellipse loses to a newer version, and keeps it', async () => {
    const user = userEvent.setup();
    editError = { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } };
    renderForm(target([text('A')]));

    await addFromMenu(user, 'Ellipse');

    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    expect(screen.getByTestId('chain-recovery-alert').textContent).not.toContain('Try again');
    expect(layerNames()).toEqual(['Ellipse 1', 'A']);
  });

  it('Keeps an added Box in place and shows the save-failure message when the server answers 403', async () => {
    const user = userEvent.setup();
    editError = { status: 403, data: {} };
    editDraftMock.mockResolvedValue({ error: { status: 403, data: {} } });
    renderForm(target([text('A')]));

    await addFromMenu(user, 'Box');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(screen.getByTestId('chain-recovery-alert').textContent).toContain('Could not save the overlay');
    expect(layerNames()).toEqual(['Box 1', 'A']);
    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'true');
  });
});
