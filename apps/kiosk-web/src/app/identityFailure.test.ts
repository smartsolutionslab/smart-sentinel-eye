import { describe, it, expect } from 'vitest';
import { ErrorResponse, ErrorTimeout } from 'oidc-client-ts';
import { classifyIdentityFailure } from './identityFailure.js';

/**
 * Spec 051 — deciding what a screen should do about a failed renewal.
 *
 * <p>
 * <b>Every case here uses the identity library's real error types</b>, built the
 * way the library builds them. A hand-rolled <c>{ error: 'x' }</c> would pass
 * against a rule that never sees a real one, and the library's shape is the
 * whole subject: which class an error is answers the wrong question, and its
 * code answers the right one.
 * </p>
 */

describe('An overloaded provider is recoverable (spec 051 T002)', () => {
  /**
   * **The defect this feature exists to avoid, and its own test on purpose.**
   *
   * <p>
   * These codes arrive on a fully-formed <c>ErrorResponse</c> from a provider
   * that is reachable and simply overloaded — the single most likely real outage
   * on a fab. A rule that branches on the error's <i>class</i> before its
   * <i>code</i> marks them terminal and leaves every screen on the wall dark
   * through an outage it would have ridden out.
   * </p>
   *
   * <p>
   * <b>It would also pass the obvious test.</b> Stopping the provider produces
   * no code at all and never reaches this branch, so a suite that induces
   * failure only that way is green with the rule inverted.
   * </p>
   */
  it.each(['server_error', 'temporarily_unavailable'])(
    'Treats %s as recoverable, because the provider answering is not the provider refusing',
    (code) => {
      const answered = new ErrorResponse({ error: code, error_description: 'try later' });

      expect(classifyIdentityFailure(answered)).toBe('recoverable');
    },
  );

  /**
   * Stated as the property rather than the cases, so the rule cannot be
   * satisfied by special-casing the two codes above while still treating "the
   * provider answered" as terminal.
   */
  it('Does not treat an answer from the provider as terminal in itself', () => {
    const answered = new ErrorResponse({ error: 'slow_down', error_description: 'back off' });

    expect(classifyIdentityFailure(answered)).toBe('recoverable');
  });
});

describe('A refused screen is terminal (spec 051 T003)', () => {
  it.each(['invalid_grant', 'invalid_client', 'unauthorized_client', 'access_denied', 'invalid_scope'])(
    'Treats %s as refused',
    (code) => {
      const refusal = new ErrorResponse({ error: code, error_description: 'no' });

      expect(classifyIdentityFailure(refusal)).toBe('refused');
    },
  );

  /**
   * The case observed against a running provider: a disabled account makes the
   * refresh-token grant answer `400 invalid_grant` with "User disabled". This is
   * the failure that today puts the provider's own login form on a wall.
   */
  it('Treats a disabled account as refused, which is what a shut-out screen actually gets', () => {
    const disabled = new ErrorResponse({ error: 'invalid_grant', error_description: 'User disabled' });

    expect(classifyIdentityFailure(disabled)).toBe('refused');
  });

  /**
   * **The asymmetric default, asserted rather than assumed** (FR-005).
   *
   * <p>
   * The two mistakes are not equal. A wrong <i>recoverable</i> costs one screen
   * a request every thirty seconds. A wrong <i>refused</i> costs a whole wall
   * its picture through an outage it would have survived, and asks someone to
   * re-commission hardware that was fine. So an unrecognised code falls to
   * recoverable, and this fails if that is ever reversed.
   * </p>
   */
  it('Treats a code nobody enumerated as recoverable rather than refused', () => {
    const unknown = new ErrorResponse({ error: 'a_code_from_a_future_provider', error_description: '?' });

    expect(classifyIdentityFailure(unknown)).toBe('recoverable');
  });
});

describe('A misconfigured wall screen is told the truth (spec 052)', () => {
  /**
   * **The case this feature creates, so this feature handles it.**
   *
   * <p>
   * A screen configured as a wall display asks for a long-lived grant. An
   * operator signing into it lacks that privilege, so the provider refuses the
   * whole sign-in with <c>not_allowed</c> — and retrying will never change which
   * account is at the keyboard.
   * </p>
   *
   * <p>
   * Without this the code is unrecognised, and unrecognised defaults to
   * recoverable — deliberately, because a wrong "terminal" darkens a wall. Here
   * that default is wrong in the other direction: the screen would retry
   * forever behind "Reconnecting", telling a passer-by the problem will clear.
   * </p>
   */
  it('Treats not_allowed as refused, because no number of attempts changes who is signed in', () => {
    const refusal = new ErrorResponse({
      error: 'not_allowed',
      error_description: 'Offline tokens not allowed for the user or client',
    });

    expect(classifyIdentityFailure(refusal)).toBe('refused');
  });

  it('Finds it through the binding wrapper too, which is how it actually arrives', () => {
    const inner = new ErrorResponse({ error: 'not_allowed', error_description: 'no' });
    const wrapped = { name: inner.name, message: inner.message, innerError: inner, source: 'renewSilent' };

    expect(classifyIdentityFailure(wrapped)).toBe('refused');
  });
});

describe('The code survives the React binding wrapping it (spec 051)', () => {
  /**
   * **The shape the application actually sees**, and the reason every earlier
   * case in this file was insufficient on its own.
   *
   * <p>
   * <c>react-oidc-context</c> does not pass on what the identity library threw.
   * It normalises the error into a fresh object — <c>name</c>, <c>message</c>,
   * <c>stack</c>, <c>source</c> — and keeps the original, the only thing holding
   * the OAuth code, under <c>innerError</c>. So <c>auth.error.error</c> is
   * always undefined.
   * </p>
   *
   * <p>
   * A classifier reading the top level therefore calls <b>every refusal
   * recoverable</b>, and a shut-out screen retries forever instead of saying it
   * has been shut out. Every unit test here passed while that was true, because
   * they all handed over the unwrapped error. The end-to-end test is what
   * disagreed.
   * </p>
   */
  const asTheBindingDelivers = (inner: unknown) => ({
    name: (inner as Error).name,
    message: (inner as Error).message,
    stack: 'irrelevant',
    innerError: inner,
    source: 'renewSilent',
  });

  it('Finds a refusal wrapped by the binding', () => {
    const wrapped = asTheBindingDelivers(
      new ErrorResponse({ error: 'invalid_grant', error_description: 'User disabled' }),
    );

    expect(classifyIdentityFailure(wrapped)).toBe('refused');
  });

  it('Finds an overload wrapped by the binding, and still calls it recoverable', () => {
    const wrapped = asTheBindingDelivers(new ErrorResponse({ error: 'server_error', error_description: 'busy' }));

    expect(classifyIdentityFailure(wrapped)).toBe('recoverable');
  });

  it('Treats a wrapped network failure as recoverable', () => {
    expect(classifyIdentityFailure(asTheBindingDelivers(new TypeError('Failed to fetch')))).toBe('recoverable');
  });

  it('Does not loop forever on an error that wraps itself', () => {
    const circular: Record<string, unknown> = { name: 'Error' };
    circular['innerError'] = circular;

    expect(classifyIdentityFailure(circular)).toBe('recoverable');
  });
});

describe('A provider that never answered is recoverable (spec 051 T004)', () => {
  /**
   * What the browser raises when the request does not leave the building. The
   * identity library rethrows it untouched — verified by reading `postForm`,
   * where a network error is caught only to be logged and rethrown.
   */
  it('Treats a network failure as recoverable', () => {
    expect(classifyIdentityFailure(new TypeError('Failed to fetch'))).toBe('recoverable');
  });

  /**
   * A provider that hangs rather than refusing. **Route interception cannot
   * produce this** — an aborted request never times out — so without this case
   * the branch ships untested (research §R5).
   */
  it('Treats a timeout as recoverable', () => {
    expect(classifyIdentityFailure(new ErrorTimeout('took too long'))).toBe('recoverable');
  });

  /**
   * A proxy or captive portal answering with HTML makes the library throw a
   * plain `Error` carrying no code. Recoverable is right: something in the way
   * is exactly the sort of thing that clears.
   */
  it.each([
    ['a plain error carrying no code', new Error('Invalid response Content-Type: text/html')],
    ['an empty code', { error: '' }],
    ['a non-string code', { error: 404 }],
    ['nothing at all', undefined],
    ['null', null],
  ])('Does not reach refused for %s', (_label, cause) => {
    expect(classifyIdentityFailure(cause)).toBe('recoverable');
  });
});
