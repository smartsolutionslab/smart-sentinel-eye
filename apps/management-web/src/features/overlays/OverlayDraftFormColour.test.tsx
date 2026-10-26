import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiSlices } from '../../app/store.js';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditTarget } from './OverlayDraftForm.js';

/**
 * Spec 463 (issue #3108) T043. New behaviour, RED (ADR-0139): choosing a swatch on the selected
 * element sends exactly one PATCH with the swatch hex; a refused hex sends nothing; a stale
 * version offers Reload, never Retry. Today the form has no colour control, so every case fails
 * on the missing swatch. The mocks mirror `OverlayDraftFormShapes.test.tsx`.
 */

const editDraftMock = vi.fn(async (_body: unknown): Promise<unknown> => ({ data: 1 }));
let editError: unknown = undefined;
let chainQueryState: { data?: { overlayIdentifier: string; version: number } } = { data: undefined };

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
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

const OVERLAY_IDENTIFIER = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

let store: ReturnType<typeof createApiStore>;

beforeEach(() => {
  store = createApiStore(apiSlices);
  editDraftMock.mockReset();
  editDraftMock.mockResolvedValue({ data: 1 });
  editError = undefined;
  chainQueryState = { data: { overlayIdentifier: OVERLAY_IDENTIFIER, version: 7 } };
});

afterEach(cleanup);

function text(): OverlayElement {
  return {
    kind: 'Text',
    color: '#9F9F9FD9',
    text: 'T1',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function renderForm(elements: OverlayElement[]) {
  const editTarget: OverlayEditTarget = {
    overlayIdentifier: OVERLAY_IDENTIFIER,
    revisionNumber: 1,
    name: 'Line 1 readings',
    elements,
  };
  return render(
    <Provider store={store}>
      <OverlayDraftForm editTarget={editTarget} onDone={() => undefined} onCancel={() => undefined} />
    </Provider>,
  );
}

function lastBody(): { elements: Array<Record<string, unknown>>; version?: number } {
  const calls = editDraftMock.mock.calls as unknown as ReadonlyArray<
    [{ elements: Array<Record<string, unknown>>; version?: number }]
  >;
  return calls[calls.length - 1]![0];
}

function hexField(): InstanceType<typeof window.HTMLInputElement> {
  return screen.getByLabelText('Colour', { exact: true }) as InstanceType<typeof window.HTMLInputElement>;
}

describe('OverlayDraftForm colour', () => {
  it('Saves a chosen cyan-700 swatch as exactly one PATCH whose element 0 colour is #0F707DFF', async () => {
    const user = userEvent.setup();
    renderForm([text()]);

    await user.click(screen.getByRole('button', { name: 'cyan-700' }));
    expect(hexField().value).toBe('#0F707DFF');
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(editDraftMock).toHaveBeenCalledTimes(1);
    const body = lastBody();
    expect(body.version).toBe(7);
    expect(body.elements[0]).toEqual({ ...text(), color: '#0F707DFF' });
  });

  it('Saves a typed lower-case hex canonicalised to #D32F2FFF', async () => {
    const user = userEvent.setup();
    renderForm([text()]);

    fireEvent.change(hexField(), { target: { value: '#d32f2f' } });
    fireEvent.keyDown(hexField(), { key: 'Enter' });
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(lastBody().elements[0]!['color']).toBe('#D32F2FFF');
  });

  it('Sends no request for a refused #FFFFFF, and says the colour is brighter than the wall allows', async () => {
    const user = userEvent.setup();
    renderForm([text()]);

    fireEvent.change(hexField(), { target: { value: '#FFFFFF' } });
    fireEvent.keyDown(hexField(), { key: 'Enter' });
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    expect(editDraftMock).not.toHaveBeenCalled();
    expect(
      screen
        .getAllByRole('alert')
        .map((alert) => alert.textContent)
        .join(' '),
    ).toMatch(/brighter than the wall allows/i);
  });

  it('Offers Reload, never Retry, when a colour change loses to a newer version', async () => {
    const user = userEvent.setup();
    editError = { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } };
    renderForm([text()]);

    await user.click(screen.getByRole('button', { name: 'cyan-700' }));

    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
    expect(screen.getByTestId('chain-recovery-alert').textContent).not.toMatch(/try again/i);
  });
});
