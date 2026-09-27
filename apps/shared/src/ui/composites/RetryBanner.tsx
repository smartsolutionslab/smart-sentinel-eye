export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
}

// Call sites differ only in the sentence (a failed load vs. stale data
// shown below), so `message` carries the distinction and there is no
// `variant` prop.
export function RetryBanner({ message, onRetry }: RetryBannerProps) {
  return (
    <div
      role="alert"
      className="mb-4 rounded-md border border-accent-fault-border bg-accent-fault-subtle px-3 py-2 text-sm text-accent-fault"
    >
      {message}{' '}
      <button type="button" className="underline" onClick={onRetry}>
        Retry
      </button>
    </div>
  );
}
