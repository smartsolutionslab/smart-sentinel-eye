/**
 * Why a screen could not renew its grant, and therefore what it should do.
 *
 * <p>
 * <b>A wall has nobody standing at it.</b> The two failures that look identical
 * from outside need opposite handling: an identity service that is briefly
 * absent resolves by itself and the screen should wait it out, while a screen
 * the provider refuses will never resolve and should say so instead of retrying
 * at a wall nobody is reading.
 * </p>
 */
export type IdentityFailureVerdict =
  /** The provider could not answer, or answered "not now". Retry; do not involve a person. */
  | 'recoverable'
  /** The provider answered and will not accept this screen. Say so; do not retry. */
  | 'refused'
  /**
   * The session ended and a person must sign in. **Not the same as refused** —
   * this is the ten-hour ceiling arriving, and announcing it as a revoked screen
   * would send someone to re-commission hardware that needed a sign-in.
   *
   * Never produced by {@link classifyIdentityFailure}: it is reached only where
   * there is no error to classify.
   */
  | 'interactive';

/**
 * The codes that mean this screen is refused and retrying cannot help.
 *
 * <p>
 * <b>An allowlist of terminal causes, not a denylist of recoverable ones</b>,
 * and that shape is the requirement rather than a preference (FR-005). Anything
 * unrecognised falls through to recoverable, because the two mistakes are not
 * equal: a wrong <i>recoverable</i> costs one screen a request every thirty
 * seconds, while a wrong <i>refused</i> costs a whole wall its picture through
 * an outage it would have survived.
 * </p>
 */
const REFUSED_CODES: readonly string[] = [
  'invalid_grant',
  'invalid_client',
  'unauthorized_client',
  'access_denied',
  'invalid_scope',
  // **What a wall-mode screen gets when the wrong account signs into it**
  // (spec 052). A screen configured as a wall display asks for a long-lived
  // grant; an account without that privilege — any operator — is refused the
  // whole sign-in with this code.
  //
  // It is terminal because no amount of retrying changes which account is
  // signed in. Left unrecognised it would default to recoverable, and a
  // misconfigured screen would sit on "Reconnecting" forever, telling whoever
  // reads it that the problem will clear. It will not: someone has to change
  // the configuration or sign in as something else.
  'not_allowed',
];

/**
 * The OAuth error code a failure carries, if it carries one.
 *
 * <p>
 * Read structurally rather than by testing the error's class. The identity
 * library raises <c>ErrorResponse</c> for a token endpoint that answered with an
 * error body, and rethrows the browser's own error untouched when the request
 * never left — but <b>which class it is answers the wrong question</b>. See
 * {@link classifyIdentityFailure}.
 * </p>
 */
const codeOf = (cause: unknown): string | undefined => {
  // **Unwrapped, because the code is not where the error is.** The React
  // binding does not hand over what the identity library threw: it normalises
  // it into a fresh object carrying `name`, `message` and `stack`, and keeps
  // the original — the only thing holding the OAuth code — under `innerError`.
  // So `auth.error.error` is always undefined, and a classifier reading it
  // calls every refusal recoverable. Read in the binding's source after an
  // end-to-end test disagreed with a passing unit test.
  for (let level: unknown = cause, depth = 0; depth < MAXIMUM_WRAPPING; depth += 1) {
    if (typeof level !== 'object' || level === null) return undefined;

    const code = (level as { error?: unknown }).error;
    if (typeof code === 'string' && code.length > 0) return code;

    level = (level as { innerError?: unknown }).innerError;
  }

  return undefined;
};

/** How far to look for the code. Two levels covers the binding's one wrap, with room. */
const MAXIMUM_WRAPPING = 4;

/**
 * What a screen should do about a failed renewal.
 *
 * <p>
 * <b>Decided by the cause the provider reports, never by whether it
 * answered.</b> That distinction is the whole rule and it is easy to get
 * backwards: <c>server_error</c> and <c>temporarily_unavailable</c> arrive on a
 * fully-formed error response from a provider that is reachable and simply
 * overloaded — the single most likely real outage on a fab. Branching on the
 * error's class would mark every one of them terminal and leave a wall dark
 * through an outage it would have ridden out.
 * </p>
 *
 * <p>
 * That mistake is also invisible to the obvious test. Stopping the provider
 * produces no code at all and never reaches this branch, so a suite that induces
 * failure only that way passes with the rule inverted.
 * </p>
 */
export const classifyIdentityFailure = (cause: unknown): Exclude<IdentityFailureVerdict, 'interactive'> => {
  const code = codeOf(cause);

  return code !== undefined && REFUSED_CODES.includes(code) ? 'refused' : 'recoverable';
};
