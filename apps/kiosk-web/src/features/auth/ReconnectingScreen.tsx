/**
 * What a wall shows while its identity service is away (spec 051 US1).
 *
 * <p>
 * <b>The message a person reads is that nothing is required of them.</b> This
 * screen replaces one that said "Sign-in failed" above the browser's own phrase
 * for a request that did not leave the building — and then waited for someone to
 * press a button. On a wall nobody is standing at, that is a dark screen until
 * the next shift.
 * </p>
 */
export function ReconnectingScreen({ attempt, onRetryNow }: { attempt: number; onRetryNow: () => void }) {
  return (
    <main
      className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary"
      // The whole screen changes at once and a person may be walking up to it
      // mid-outage, so the state is announced rather than silently repainted.
      role="status"
      aria-live="polite"
      data-testid="identity-reconnecting"
    >
      <h1 className="text-3xl font-semibold">Reconnecting</h1>
      <p className="text-fg-muted">
        The sign-in service cannot be reached. This screen is retrying and will come back on its own.
      </p>
      <p className="text-fg-muted">No action is needed.</p>

      {/*
        Kept for the case where somebody is standing here and would rather not
        wait out the current interval. It resets the schedule rather than
        starting a second one (FR-013).
      */}
      <button
        type="button"
        className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
        onClick={onRetryNow}
      >
        Try now
      </button>

      <p className="text-sm text-fg-muted" data-testid="identity-reconnecting-attempt">
        Attempt {attempt}
      </p>
    </main>
  );
}
