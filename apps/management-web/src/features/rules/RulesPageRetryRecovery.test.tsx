import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse } from '@smart-sentinel-eye/shared/test/responses';

// gateway.ts resolves the API origin at module load; stub it before the store
// (and with it every api slice) is imported.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

// RulesPage renders RuleDialog, which reads the operator's fabs from the OIDC claims.
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: ['/fabs/munich'] } } }),
}));

const { store, resetApiCaches } = await import('../../app/store.js');
const { RulesPage } = await import('./RulesPage.js');

/**
 * Spec 429 (#3073) R15 and R17, phase 4a RED. The app's real store, `fetch`
 * stubbed and routed by URL. R15: a refused read then a successful retry must
 * leave the page heading focused (today it stays on `document.body`). R17: while
 * the retried read is still in flight Retry must stay mounted, the same node,
 * focused (today `isError` unmounts it the moment the click lands).
 */

function rule() {
  return {
    ruleIdentifier: '019f-aaaa',
    version: 0,
    fab: 'munich',
    name: 'high-oee',
    triggerSource: 'plc',
    triggerKind: 'PlcCycleStart',
    predicate: '$.payload.cycleTime <= 30',
    action: {
      kind: 'SetVariableValue',
      variableName: 'oeeLine1',
      valueExpression: '100 - $.payload.cycleTime * 2',
      overlay: null,
      durationMs: null,
    },
    state: 'Draft',
    createdAt: '2026-05-28T08:00:00Z',
    createdBy: '019f-bbbb',
    publishedAt: null,
    archivedAt: null,
  };
}

/** Routes by URL: only the rules list is scripted, the page's other reads get empty answers. */
function stubFetch(rulesResponder: () => Promise<Response>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((request: Request) => {
      if (request.url.includes('automation/rules')) {
        return rulesResponder();
      }
      if (request.url.includes('layout-composition/layouts')) {
        return Promise.resolve(jsonResponse({ chains: [], published: [] }));
      }
      return Promise.resolve(jsonResponse([]));
    }),
  );
}

function renderPage() {
  return render(
    <Provider store={store}>
      <RulesPage />
    </Provider>,
  );
}

describe('RulesPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    // Unmount first: a mounted page would re-subscribe the moment the cache is reset.
    cleanup();
    resetApiCaches(store.dispatch);
  });

  it('Focuses the page heading when a retried read succeeds', async () => {
    const user = userEvent.setup();
    let rulesCalls = 0;
    stubFetch(() => {
      rulesCalls += 1;
      return Promise.resolve(
        rulesCalls === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse([rule()]),
      );
    });
    renderPage();

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('high-oee');

    expect(screen.getByRole('heading', { level: 1, name: 'Rules' })).toHaveFocus();
  });

  it('Keeps the same focused Retry control mounted while the retried read is in flight', async () => {
    const user = userEvent.setup();
    let rulesCalls = 0;
    let release: (response: Response) => void = () => {};
    stubFetch(() => {
      rulesCalls += 1;
      if (rulesCalls === 1) {
        return Promise.resolve(jsonResponse({ title: 'PROBLEM', status: 500 }, 500));
      }
      return new Promise<Response>((resolve) => {
        release = resolve;
      });
    });
    renderPage();

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await waitFor(() => expect(rulesCalls).toBe(2));
    await act(async () => {
      await Promise.resolve();
    });

    const inFlight = screen.queryByRole('button', { name: 'Retry' });
    expect(inFlight).toBe(retry);
    expect(inFlight).toHaveFocus();

    release(jsonResponse([rule()]));
    await screen.findByText('high-oee');
  });
});
