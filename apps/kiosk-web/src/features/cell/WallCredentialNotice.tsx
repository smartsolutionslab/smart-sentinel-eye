import type { WallCredentialState } from './useWallCredential.js';

export interface WallCredentialNoticeProps {
  state: Exclude<WallCredentialState, 'idle'>;
}

const RENEWING = "This display's access was refused. Reconnecting with a new credential.";
const REFUSED = 'Access refused. Ask an administrator to re-enrol this display.';

/** Calm, static and dark (ADR-0146): no motion, no bright fill. */
export function WallCredentialNotice({ state }: WallCredentialNoticeProps) {
  return (
    <div
      data-testid="wall-credential-state"
      role={state === 'refused' ? 'alert' : 'status'}
      className="absolute inset-x-0 top-14 z-10 mx-auto w-fit max-w-[90%] rounded-md border border-fg-muted/30 bg-bg-base px-6 py-3 text-center text-lg text-fg-muted"
    >
      {state === 'refused' ? REFUSED : RENEWING}
    </div>
  );
}
