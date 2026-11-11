import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store';

/**
 * Spec 481 T003. What the form's `getToken` resolves when there is no token.
 * `OverlayDraftFormGetToken.test` pins the identity and the renewed value; nothing
 * asked about a missing token, so `null` could become `''` or `undefined`
 * unnoticed, and the editor passes that value to a WHEP session as the bearer.
 * `auth?.`: the real `useAuth()` returns `undefined` with no provider mounted.
 * Green on today's code.
 */

const auth = vi.hoisted(() => ({
  current: undefined as undefined | { user?: { access_token?: string } },
}));

vi.mock('react-oidc-context', () => ({ useAuth: () => auth.current }));

const editorRenders = vi.hoisted(() => [] as { getToken?: () => Promise<string | null> }[]);

vi.mock('@smart-sentinel-eye/shared/ui/composites/OverlayEditor', () => ({
  OverlayEditor: (props: { getToken?: () => Promise<string | null> }) => {
    editorRenders.push(props);
    return <div data-testid="overlay-editor-stub" />;
  },
}));

const { OverlayDraftForm } = await import('./OverlayDraftForm');

function tree() {
  return (
    <Provider store={store}>
      <OverlayDraftForm onDone={() => {}} onCancel={() => {}} />
    </Provider>
  );
}

describe("OverlayDraftForm's getToken without a token", () => {
  beforeEach(() => {
    editorRenders.length = 0;
  });

  it('resolves null when there is no auth provider', async () => {
    auth.current = undefined;

    render(tree());

    await expect(editorRenders.at(0)!.getToken!()).resolves.toBeNull();
  });

  it('resolves null for a user who holds no access token', async () => {
    auth.current = { user: {} };

    render(tree());

    await expect(editorRenders.at(0)!.getToken!()).resolves.toBeNull();
  });
});
