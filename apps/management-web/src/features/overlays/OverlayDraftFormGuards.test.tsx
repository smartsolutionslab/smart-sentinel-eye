import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditTarget } from './OverlayDraftForm';

/**
 * Spec 485 (#3226). Characterisation, green on today's code.
 *
 * <p>
 * `OverlayLayerList` and `useElementSelection` both refuse a Remove or Move while `readOnly`, and both refuse to remove the last
 * element. The list's guards are pinned by the list's own tests and the hook's by `OverlayDraftFormProps`' stub seam, but each break is
 * invisible through the other: with BOTH removed, no test that renders the real list inside the real form noticed. The buttons are
 * `aria-disabled`, not natively disabled, so a click on one still reaches the handler: these tests press them.
 * </p>
 */

const createDraftMock = vi.fn(async (_body: unknown) => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useGetOverlayQuery: () => ({
      data: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 },
      currentData: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 },
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

const { OverlayDraftForm } = await import('./OverlayDraftForm');

const OVERLAY_IDENTIFIER = 'dddddddd-dddd-dddd-dddd-dddddddddddd';

let store: ReturnType<typeof createApiStore>;
let uncaught: unknown[];
const recordUncaught = (event: { error?: unknown; preventDefault: () => void }) => {
  uncaught.push(event.error ?? event);
  event.preventDefault();
};

beforeEach(() => {
  store = createApiStore(apiSlices);
  uncaught = [];
  window.addEventListener('error', recordUncaught);
});

afterEach(() => {
  window.removeEventListener('error', recordUncaught);
  cleanup();
});

function text(value: string, y: number): OverlayElement {
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

function renderForm(props: Partial<Parameters<typeof OverlayDraftForm>[0]>) {
  return render(
    <Provider store={store}>
      <OverlayDraftForm onDone={() => undefined} onCancel={() => undefined} {...props} />
    </Provider>,
  );
}

function layerNames(): string[] {
  return within(screen.getByRole('list', { name: 'Layers' }))
    .getAllByRole('listitem')
    .map((item) => within(item).getAllByRole('button')[0]!.textContent ?? '');
}

describe('OverlayDraftForm — a pressed unavailable layer control changes nothing (spec 485)', () => {
  it('removes nothing and says nothing when Remove is pressed while read-only', async () => {
    const user = userEvent.setup();
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]), readOnly: true });
    const before = layerNames();

    await user.click(screen.getByRole('button', { name: 'Remove A' }));

    expect(layerNames(), 'a read-only form keeps both layers').toEqual(before);
    expect(screen.getByTestId('overlay-layer-list-live-region'), 'no removal is announced').toHaveTextContent('');
    expect(uncaught).toEqual([]);
  });

  it('reorders nothing and says nothing when Move is pressed while read-only', async () => {
    const user = userEvent.setup();
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]), readOnly: true });
    const before = layerNames();

    await user.click(screen.getByRole('button', { name: 'Move B down' }));
    await user.click(screen.getByRole('button', { name: 'Move A up' }));

    expect(layerNames(), 'a read-only form keeps the layer order').toEqual(before);
    expect(screen.getByTestId('overlay-layer-list-live-region'), 'no move is announced').toHaveTextContent('');
    expect(uncaught).toEqual([]);
  });

  it('keeps the last element and throws nothing when Remove is pressed on it', async () => {
    const user = userEvent.setup();
    renderForm({ editTarget: target([text('Only', 0.1)]) });

    await user.click(screen.getByRole('button', { name: 'Remove Only' }));

    expect(layerNames(), 'the only layer stays').toEqual(['Only']);
    expect(uncaught, 'removing the last layer must not even be attempted').toEqual([]);
  });
});
