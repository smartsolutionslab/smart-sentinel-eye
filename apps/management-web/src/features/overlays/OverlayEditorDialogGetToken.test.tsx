import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';

/**
 * Spec 147 T004 / R5 — new behaviour, RED.
 *
 * <p>
 * <b>Why `OverlayEditor` is mocked here, and nowhere else in this spec's
 * tests.</b> This is the one T004 assertion that is about `OverlayEditorDialog`
 * itself, not about the capture pipeline underneath it: `OverlayEditorDialog`
 * must build a `getToken` whose <i>identity</i> survives a silent token renewal
 * — copying `CameraDetailPage.tsx:20-38`'s ref-behind-`useCallback` shape,
 * including its reasoning (T010). That is exactly what `CameraDetailPage.test.tsx`
 * proves of `CameraDetailPage` by mocking `CameraViewer` to capture the prop
 * across renders, and the same technique is used here. It cannot share a file
 * with the tests that drive a real capture (`OverlayEditorDialog.test.tsx`):
 * those need the real `OverlayEditor`, and `vi.mock` replaces a module for an
 * entire file.
 * </p>
 *
 * <p>
 * The risk this guards: an unstable `getToken` identity is what silently killed
 * the decode sampler once already (issue 1889) — nothing visible fails, a
 * dependent effect just stops firing a second time.
 * </p>
 */

const ACCESS_TOKEN = 'operator-access-token';
const RENEWED_TOKEN = 'operator-access-token-after-silent-renew';

const currentToken = { value: ACCESS_TOKEN };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: currentToken.value } }),
}));

const editorRenders = vi.hoisted(() => [] as { getToken?: () => Promise<string | null> }[]);

vi.mock('@smart-sentinel-eye/shared/ui/composites/OverlayEditor', () => ({
  OverlayEditor: (props: { getToken?: () => Promise<string | null> }) => {
    editorRenders.push(props);
    return <div data-testid="overlay-editor-stub" />;
  },
}));

const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

function dialogTree() {
  return (
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>
  );
}

function renderDialog() {
  return render(dialogTree());
}

describe("OverlayEditorDialog's getToken identity (spec 147 R5)", () => {
  beforeEach(() => {
    editorRenders.length = 0;
    currentToken.value = ACCESS_TOKEN;
  });

  it('Hands OverlayEditor a getToken that resolves the current token', async () => {
    renderDialog();

    const first = editorRenders.at(0);
    expect(first).toBeDefined();
    expect(first!.getToken).toBeDefined();
    await expect(first!.getToken!()).resolves.toBe(ACCESS_TOKEN);
  });

  it('Keeps the same getToken identity across a re-render and a silent token renewal', async () => {
    const { rerender } = renderDialog();

    const first = editorRenders.at(0);
    expect(first?.getToken).toBeDefined();

    // A re-render of the same tree with nothing else changed — the weaker
    // half, which would catch a fresh inline arrow rebuilt every render.
    rerender(dialogTree());
    expect(editorRenders.length).toBeGreaterThan(1);
    expect(editorRenders.at(-1)!.getToken).toBe(first!.getToken);

    // Then the renewal. Same function, new token underneath it.
    currentToken.value = RENEWED_TOKEN;
    rerender(dialogTree());

    expect(editorRenders.at(-1)!.getToken).toBe(first!.getToken);
    await expect(first!.getToken!()).resolves.toBe(RENEWED_TOKEN);
  });
});
