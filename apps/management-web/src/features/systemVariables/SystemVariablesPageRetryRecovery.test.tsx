import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse } from '@smart-sentinel-eye/shared/test/responses';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches systemVariables.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: ['/fabs/munich'] } } }),
}));

const { systemVariablesApi } = await import('@smart-sentinel-eye/shared/api/systemVariables.api');
const { SystemVariablesPage } = await import('./SystemVariablesPage.js');

/**
 * Spec 429 (#3073) R8, phase 4a RED. Real `systemVariablesApi`, `fetch` stubbed
 * at the network boundary: the first read is refused, the retried one succeeds,
 * and the page heading must take focus (today it stays on `document.body`).
 */

function variable() {
  return {
    variableIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'oeeLine1',
    type: 'Number',
    state: 'Defined',
    value: null,
    truthyLabel: null,
    falsyLabel: null,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
  };
}

function createStore() {
  return createApiStore([systemVariablesApi]);
}

describe('SystemVariablesPage retry recovery (spec 429)', () => {
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
          callCount === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse([variable()]),
        );
      }),
    );
    render(
      <Provider store={createStore()}>
        <SystemVariablesPage />
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByRole('heading', { name: 'oeeLine1' });

    expect(screen.getByRole('heading', { level: 1, name: 'System variables' })).toHaveFocus();
  });
});
