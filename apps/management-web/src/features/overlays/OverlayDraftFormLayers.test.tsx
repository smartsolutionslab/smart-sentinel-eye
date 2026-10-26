import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store.js';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditTarget } from './OverlayDraftForm.js';

/**
 * Spec 441 (issue #3072) slice 1, C4. New behaviour, RED (ADR-0139): the form
 * holds several labels through `useFieldArray`, with a layer list. The
 * mocks mirror `OverlayDraftForm.test.tsx`; the existing FR-019 guards there
 * stay untouched.
 */

const createDraftMock = vi.fn(async (_body: unknown) => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));
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

const OVERLAY_IDENTIFIER = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

let store: ReturnType<typeof createApiStore>;

beforeEach(() => {
  store = createApiStore(apiSlices);
  createDraftMock.mockClear();
  editDraftMock.mockClear();
  editError = undefined;
  chainQueryState = { data: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 } };
});

afterEach(cleanup);

function text(
  value: string,
  geometry: { x?: number; y?: number; width?: number; height?: number } = {},
): OverlayElement {
  return {
    kind: 'Text',
    color: '#9F9F9FD9',
    text: value,
    normalizedX: geometry.x ?? 0.1,
    normalizedY: geometry.y ?? 0.1,
    normalizedWidth: geometry.width ?? 0.3,
    normalizedHeight: geometry.height ?? 0.1,
    fontSizePx: 32,
  };
}

function box(): OverlayElement {
  return {
    kind: 'Box',
    color: '#FF0000FF',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
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

/** Spec 457: the single "Add label" button became an "Add" menu; choosing Text is the same act. */
async function addLabel(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Add' }));
  await user.click(await screen.findByRole('menuitem', { name: 'Text' }));
}

async function replaceLabelText(user: ReturnType<typeof userEvent.setup>, value: string) {
  const field = screen.getByTestId('overlay-editor-text');
  await user.clear(field);
  await user.type(field, value);
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

describe('OverlayDraftForm layers - create', () => {
  it('Lists the single default label', () => {
    renderForm();
    expect(layerNames()).toEqual(['Overlay text']);
  });

  it('Adds a label at the front, selects it, and saves both in list order without a client id', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/name/i), 'Line 1 readings');
    await replaceLabelText(user, 'T1');
    await addLabel(user);

    expect(layerNames()).toEqual(['Overlay text', 'T1']);
    expect(layerChip('Overlay text')).toHaveAttribute('aria-pressed', 'true');

    await replaceLabelText(user, 'T2');
    expect(layerNames()).toEqual(['T2', 'T1']);

    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const { elements } = lastBody(createDraftMock);
    expect(elements.map((element) => element['text'])).toEqual(['T1', 'T2']);
    elements.forEach((element) => expect(Object.keys(element)).not.toContain('id'));
  });

  it('Makes Add unavailable at 8 elements and says why', async () => {
    const user = userEvent.setup();
    renderForm();
    for (let added = 0; added < 7; added++) {
      await addLabel(user);
    }
    expect(within(layerList()).getAllByRole('listitem')).toHaveLength(8);

    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Up to 8 elements')).toBeInTheDocument();
    await user.click(add);
    expect(within(layerList()).getAllByRole('listitem')).toHaveLength(8);
  });

  it('Makes Remove unavailable while a single element remains', async () => {
    const user = userEvent.setup();
    renderForm();
    const remove = screen.getByRole('button', { name: 'Remove Overlay text' });
    expect(remove).toHaveAttribute('aria-disabled', 'true');
    await user.click(remove);
    expect(layerNames()).toEqual(['Overlay text']);
  });
});

describe('OverlayDraftForm layers - edit', () => {
  it('Selects the first Text element on open and lists a shape unselected', () => {
    renderForm(target([box(), text('Line 1')]));
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Line 1');
    expect(layerChip('Line 1')).toHaveAttribute('aria-pressed', 'true');
    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'false');
  });

  it('Edits only the selected label', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.5 })]));

    await user.click(layerChip('B'));
    await replaceLabelText(user, 'B2');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    const { elements } = lastBody(editDraftMock);
    expect(elements[0]).toEqual(text('A'));
    expect(elements[1]!['text']).toBe('B2');
  });

  it('Shows the geometry inspector, not a label text field, when a shape is selected, and saves the shape unchanged', async () => {
    const user = userEvent.setup();
    renderForm(target([box(), text('Zone A')]));

    await user.click(layerChip('Box 1'));
    expect(screen.getByLabelText('Left', { exact: true })).toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-shape-selected-notice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-text')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    expect(lastBody(editDraftMock).elements).toEqual([box(), text('Zone A')]);
  });

  it('Selects the first element and makes it editable when the set has no Text element', () => {
    renderForm(target([box()]));
    expect(layerChip('Box 1')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Left', { exact: true })).toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-dialog-shapes-only-notice')).not.toBeInTheDocument();
  });

  it('Sends the whole array in the moved order under the chain version, without a client id', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.5 }), box()]));

    await user.click(layerChip('A'));
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(editDraftMock).toHaveBeenCalledTimes(1);
    const body = lastBody(editDraftMock);
    expect(body.version).toBe(7);
    expect(body.elements).toEqual([text('B', { y: 0.5 }), text('A'), box()]);
  });

  it('Keeps the moved label selected and editable after a move', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.5 })]));

    await user.click(layerChip('A'));
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    expect(layerNames()).toEqual(['A', 'B']);
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('A');

    await replaceLabelText(user, 'A2');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    const { elements } = lastBody(editDraftMock);
    expect(elements[0]!['text']).toBe('B');
    expect(elements[1]!['text']).toBe('A2');
  });

  it('Removes the selected element and selects the row now at that list position', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.3 }), text('C', { y: 0.6 })]));

    await user.click(layerChip('B'));
    await user.click(screen.getByRole('button', { name: 'Remove B' }));

    expect(layerNames()).toEqual(['C', 'A']);
    expect(layerChip('A')).toHaveAttribute('aria-pressed', 'true');
  });

  it('Keeps undo history per element: switching selection starts a fresh history', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.5 })]));

    await user.click(layerChip('A'));
    await user.type(screen.getByTestId('overlay-editor-text'), 'x');
    expect(screen.getByTestId('overlay-editor-undo')).toHaveAttribute('aria-disabled', 'false');

    await user.click(layerChip('B'));
    expect(screen.getByTestId('overlay-editor-undo')).toHaveAttribute('aria-disabled', 'true');

    await user.click(layerChip('Ax'));
    expect(screen.getByTestId('overlay-editor-undo')).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('OverlayDraftForm layers - overlap warning', () => {
  it('Warns once, naming both labels, for two overlapping Text elements, and never blocks Save', () => {
    renderForm(target([text('A', { x: 0.1, y: 0.1, width: 0.3, height: 0.1 }), text('B', { x: 0.2, y: 0.15 })]));

    const warnings = screen.getAllByTestId('overlay-editor-overlap-warning');
    expect(warnings).toHaveLength(1);
    expect(screen.getByTestId('overlay-editor-overlap-status')).toContainElement(warnings[0]!);
    expect(warnings[0]).toHaveTextContent(/A/);
    expect(warnings[0]).toHaveTextContent(/B/);
    expect(warnings[0]).toHaveTextContent(/overlap/i);
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'false');
  });

  it('Does not warn when two Text elements only touch at an edge', () => {
    renderForm(target([text('A', { x: 0.1, width: 0.3 }), text('B', { x: 0.4, width: 0.3 })]));
    expect(screen.queryByTestId('overlay-editor-overlap-warning')).not.toBeInTheDocument();
  });

  it('Never names a Box, even one covering a label', () => {
    renderForm(
      target([text('A'), { ...box(), normalizedX: 0, normalizedY: 0, normalizedWidth: 1, normalizedHeight: 1 }]),
    );
    expect(screen.queryByTestId('overlay-editor-overlap-warning')).not.toBeInTheDocument();
  });

  it('Clears the warning once the labels no longer overlap', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A', { x: 0.1, width: 0.3 }), text('B', { x: 0.2, width: 0.3, y: 0.15 })]));
    expect(screen.getAllByTestId('overlay-editor-overlap-warning')).toHaveLength(1);

    await user.click(layerChip('B'));
    await user.clear(screen.getByLabelText('Top'));
    await user.type(screen.getByLabelText('Top'), '80');
    await user.tab();

    expect(screen.queryByTestId('overlay-editor-overlap-warning')).not.toBeInTheDocument();
  });
});

describe('OverlayDraftForm layers - validation and conflict', () => {
  it('Sends nothing and selects the invalid element when one of several labels is cleared', async () => {
    const user = userEvent.setup();
    renderForm(target([text('A'), text('B', { y: 0.5 })]));

    await user.click(layerChip('B'));
    await user.clear(screen.getByTestId('overlay-editor-text'));
    await user.click(layerChip('A'));
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(editDraftMock).not.toHaveBeenCalled();
    expect(screen.getByTestId('overlay-editor-dialog-label-error')).toHaveTextContent(/text is required/i);
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('');
    expect(layerChip('Empty label')).toHaveAttribute('aria-pressed', 'true');
  });

  it('Offers Reload, never Try again, when a reordered save loses to a newer version', async () => {
    const user = userEvent.setup();
    editError = { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } };
    renderForm(target([text('A'), text('B', { y: 0.5 })]));

    await user.click(layerChip('A'));
    await user.click(screen.getByRole('button', { name: 'Move A up' }));

    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    expect(screen.getByTestId('chain-recovery-alert').textContent).not.toContain('Try again');
    expect(layerNames()).toEqual(['A', 'B']);
  });
});

describe('OverlayDraftForm layers - editor stability', () => {
  it('Mounts the label editor once and keeps it through the form seeding itself', async () => {
    // A remount between a browser locator resolving the label and measuring it
    // leaves the measurement null (the overlays.spec.ts undo flake).
    const container = document.createElement('div');
    document.body.appendChild(container);
    let removed = 0;
    const observer = new window.MutationObserver((records) => {
      for (const record of records) {
        record.removedNodes.forEach((node) => {
          if (node instanceof window.Element && node.querySelector('[data-testid="overlay-editor-label"]') !== null)
            removed++;
          if (node instanceof window.Element && node.getAttribute('data-testid') === 'overlay-editor-label') removed++;
        });
      }
    });
    observer.observe(container, { childList: true, subtree: true });

    render(
      <Provider store={store}>
        <OverlayDraftForm editTarget={target([text('A')])} onDone={() => undefined} onCancel={() => undefined} />
      </Provider>,
      { container },
    );
    await Promise.resolve();

    expect(screen.getByTestId('overlay-editor-label')).toBeInTheDocument();
    expect(removed).toBe(0);
    observer.disconnect();
    container.remove();
  });
});
