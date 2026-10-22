import { addListener, createAction } from '@reduxjs/toolkit';
import { createApi, fakeBaseQuery } from '@reduxjs/toolkit/query';
import { afterEach, describe, expect, it, vi } from 'vitest';
const configureStoreSpy = vi.hoisted(() => vi.fn());

vi.mock('@reduxjs/toolkit', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@reduxjs/toolkit')>();
  configureStoreSpy.mockImplementation(actual.configureStore as never);
  return { ...actual, configureStore: configureStoreSpy };
});

import { createApiStore } from './createApiStore.js';

/**
 * Plan.md §4.1 — `createApiStore` is the generic RTK store contract moved out
 * of `management-web/src/app/store.ts` (spec 316, T003) so every app can
 * build its store from its own list of RTK Query slices the same way.
 */

const alphaApi = createApi({
  reducerPath: 'alphaApi',
  baseQuery: fakeBaseQuery(),
  endpoints: () => ({}),
});

const betaApi = createApi({
  reducerPath: 'betaApi',
  baseQuery: fakeBaseQuery(),
  endpoints: () => ({}),
});

describe('createApiStore', () => {
  it('mounts every given slice under its own reducerPath', () => {
    const store = createApiStore([alphaApi, betaApi]);

    expect(Object.keys(store.getState()).sort()).toEqual(['alphaApi', 'betaApi']);
  });

  it('wires a listener middleware into the store, so addListener/effect dispatches work', async () => {
    const store = createApiStore([alphaApi, betaApi]);
    const probe = createAction('probe');
    let effectCalls = 0;

    store.dispatch(
      addListener({
        actionCreator: probe,
        effect: () => {
          effectCalls += 1;
        },
      }),
    );
    store.dispatch(probe());
    await Promise.resolve();

    expect(effectCalls).toBe(1);
  });

  describe('Redux DevTools', () => {
    afterEach(() => {
      vi.unstubAllEnvs();
      configureStoreSpy.mockClear();
    });

    // The RTK cache can hold a credential-bearing camera address while its dialog is open (spec 438).
    function devToolsOptionWhen(isDevelopmentBuild: boolean): unknown {
      vi.stubEnv('DEV', isDevelopmentBuild);
      createApiStore([alphaApi]);
      return (configureStoreSpy.mock.calls[0]?.[0] as { devTools?: unknown }).devTools;
    }

    it('Is not attached in a production build', () => {
      expect(devToolsOptionWhen(false)).toBe(false);
    });

    it('Is attached in a development build', () => {
      expect(devToolsOptionWhen(true)).toBe(true);
    });
  });
});
