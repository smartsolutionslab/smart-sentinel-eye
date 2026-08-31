/**
 * What a screen shows once the identity service has refused it (spec 051 US2).
 *
 * <p>
 * <b>The load-bearing property is what is absent.</b> Today this case renders
 * the identity provider's own login form: a username and password prompt on a
 * display bolted to a factory wall, inviting anyone walking past to type
 * credentials into it, and telling whoever maintains the fab nothing about what
 * is wrong. This screen exists so that never happens, and its test asserts the
 * absence of those fields rather than the presence of this text.
 * </p>
 *
 * <p>
 * It does not retry. Retrying cannot help, and a screen that kept saying it was
 * reconnecting would be lying to whoever reads it.
 * </p>
 */
export function NotAuthorizedScreen() {
  return (
    <main
      className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary"
      role="alert"
      data-testid="identity-not-authorized"
    >
      <h1 className="text-3xl font-semibold">This screen is no longer authorized</h1>
      <p className="text-fg-muted">The sign-in service has refused this display. Waiting will not restore it.</p>
      <p className="text-fg-muted">Someone needs to re-commission this screen before it can show a wall again.</p>
    </main>
  );
}
