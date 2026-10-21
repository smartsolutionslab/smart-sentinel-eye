import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches the api slices.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { scope: 'sse.events.read' } }),
}));

const { createApiStore } = await import('@smart-sentinel-eye/shared/store');
const { eventIngestionApi } = await import('@smart-sentinel-eye/shared/api/eventIngestion.api');
const { eventTypesApi } = await import('@smart-sentinel-eye/shared/api/eventTypes.api');
const { HeldEventTypesPage } = await import('./HeldEventTypesPage.js');

/**
 * Spec 429 (#3073) R16, phase 4a RED. Real api slices in a real store, `fetch`
 * stubbed: the first read is refused, the retried one succeeds, and the page
 * heading must take focus (today it stays on `document.body`).
 */

function heldRow() {
  return {
    deadLetterIdentifier: 'dl-1',
    topic: 'fab/munich/plc/line-1',
    rawPayload: '{}',
    error: 'unregistered type',
    rejectedAt: '2026-01-01T00:00:00Z',
    fab: 'munich',
    reason: 'UnknownEventType',
    kind: 'TemperatureAlarm',
    state: 'Held',
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('HeldEventTypesPage retry recovery (spec 429)', () => {
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
          callCount === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse([heldRow()]),
        );
      }),
    );
    render(
      <Provider store={createApiStore([eventIngestionApi, eventTypesApi])}>
        <HeldEventTypesPage />
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('TemperatureAlarm');

    expect(screen.getByRole('heading', { level: 1, name: 'Event types' })).toHaveFocus();
  });
});
