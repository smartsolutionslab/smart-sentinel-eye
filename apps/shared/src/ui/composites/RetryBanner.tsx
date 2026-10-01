import { FaultNotice } from './FaultNotice.js';

export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
}

// Call sites differ only in the sentence (a failed load vs. stale data
// shown below), so `message` carries the distinction and there is no
// `variant` prop.
export function RetryBanner({ message, onRetry }: RetryBannerProps) {
  return (
    <FaultNotice>
      {message}{' '}
      <button type="button" className="underline" onClick={onRetry}>
        Retry
      </button>
    </FaultNotice>
  );
}
