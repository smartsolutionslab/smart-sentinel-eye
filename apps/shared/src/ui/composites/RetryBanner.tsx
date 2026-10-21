import { LinkButton } from '../primitives/LinkButton.js';
import { FaultNotice } from './FaultNotice.js';

export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
  failed?: boolean;
  retrying?: boolean;
  onRecovered?: () => void;
}

// Call sites differ only in the sentence (a failed load vs. stale data
// shown below), so `message` carries the distinction and there is no
// `variant` prop.
export function RetryBanner({ message, onRetry }: RetryBannerProps) {
  return (
    <FaultNotice>
      {message} <LinkButton onClick={onRetry}>Retry</LinkButton>
    </FaultNotice>
  );
}
