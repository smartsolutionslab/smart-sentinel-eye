import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches audit.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { auditApi } = await import('@smart-sentinel-eye/shared/api/audit.api');
const { AuditPage } = await import('./AuditPage.js');

/**
 * Spec 429 (#3073) R12, phase 4a RED. Real `auditApi`, `fetch` stubbed: the
 * first search is refused, the retried one succeeds, and the page heading must
 * take focus (today it stays on `document.body`).
 */

function searchResult() {
  return {
    rows: [
      {
        auditIdentifier: '11111111-1111-1111-1111-111111111111',
        occurredAt: '2026-05-30T10:00:00Z',
        receivedAt: '2026-05-30T10:00:00Z',
        fab: 'munich',
        eventKind: 'CameraRegisteredV1',
        resourceKind: 'camera',
        resourceIdentifier: '33333333-3333-3333-3333-333333333333',
        actorIdentifier: '22222222-2222-2222-2222-222222222222',
        actorIsSystem: false,
        actorUsername: 'admin@munich.test',
        eventIdentifier: '44444444-4444-4444-4444-444444444444',
        payload: '{"cameraIdentifier":"33333333-3333-3333-3333-333333333333"}',
        payloadSizeBytes: 2,
        schemaVersion: 1,
      },
    ],
    nextCursor: null,
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function createStore() {
  // useRevocationFallback observes settlements via a dispatched listener and
  // throws without this middleware in the store.
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [auditApi.reducerPath]: auditApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(auditApi.middleware),
  });
}

describe('AuditPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Focuses the page heading when a retried read succeeds', async () => {
    const user = userEvent.setup();
    let callCount = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(() => {
        callCount += 1;
        return Promise.resolve(
          callCount === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse(searchResult()),
        );
      }),
    );
    render(
      <Provider store={createStore()}>
        <AuditPage />
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('CameraRegisteredV1');

    expect(screen.getByRole('heading', { level: 1, name: 'Audit' })).toHaveFocus();
  });
});
