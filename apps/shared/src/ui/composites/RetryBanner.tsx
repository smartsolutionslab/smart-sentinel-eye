import { useEffect, useRef, useState } from 'react';
import { LinkButton } from '../primitives/LinkButton.js';
import { FaultNotice } from './FaultNotice.js';

export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
  /** The query's `error !== undefined`. Defaults to `true`. */
  failed?: boolean;
  /** The query's `isFetching`. Defaults to `false`. */
  retrying?: boolean;
  /** Called once when a read started by Retry succeeds. */
  onRecovered?: () => void;
}

// Call sites differ only in the sentence (a failed load vs. stale data
// shown below), so `message` carries the distinction and there is no
// `variant` prop.
//
// The live region is a polite `aria-live` div with no role, not
// `role="status"`: several pages already query an unscoped `status` and
// must keep finding exactly one.
export function RetryBanner({ message, onRetry, failed = true, retrying = false, onRecovered }: RetryBannerProps) {
  const [announcement, setAnnouncement] = useState({ text: '', token: 0 });
  // Bumped only when Retry's own read is refused again, so the first
  // click -> in-flight transition keeps the same node (and Retry its focus).
  const [failureKey, setFailureKey] = useState(0);
  const retryRef = useRef<HTMLButtonElement>(null);
  const wasRetryingRef = useRef(retrying);
  const isFirstFailureKeyRenderRef = useRef(true);
  // Per-FETCH, never latched: an unrequested read (refetch on focus, a
  // promote refetch) must not be mistaken for the click.
  const activatedRef = useRef(false);
  const currentFetchStartedByRetryRef = useRef(false);

  useEffect(() => {
    const wasRetrying = wasRetryingRef.current;
    wasRetryingRef.current = retrying;

    if (!wasRetrying && retrying) {
      currentFetchStartedByRetryRef.current = activatedRef.current;
      activatedRef.current = false;
      if (currentFetchStartedByRetryRef.current) {
        setAnnouncement((previous) => ({ text: 'Retrying…', token: previous.token + 1 }));
      }
      return;
    }

    if (!wasRetrying || retrying) return;

    const startedByRetry = currentFetchStartedByRetryRef.current;
    currentFetchStartedByRetryRef.current = false;
    if (!startedByRetry) return;

    if (failed) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reacting to an external query's fetch lifecycle
      setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
      setFailureKey((previous) => previous + 1);
    } else {
      setAnnouncement((previous) => ({ text: 'Loaded.', token: previous.token + 1 }));
      onRecovered?.();
    }
  }, [retrying, failed, onRecovered]);

  useEffect(() => {
    if (isFirstFailureKeyRenderRef.current) {
      isFirstFailureKeyRenderRef.current = false;
      return;
    }
    retryRef.current?.focus();
  }, [failureKey]);

  const activate = () => {
    if (retrying) return;
    activatedRef.current = true;
    onRetry();
  };

  return (
    <>
      {failed && (
        <FaultNotice key={failureKey}>
          {message}{' '}
          <LinkButton
            ref={retryRef}
            aria-disabled={retrying}
            className="aria-disabled:cursor-progress"
            onClick={activate}
          >
            Retry
          </LinkButton>
        </FaultNotice>
      )}
      <div aria-live="polite" className="sr-only" data-testid="retry-banner-live-region">
        <span key={announcement.token}>{announcement.text}</span>
      </div>
    </>
  );
}
