export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
}

// Spec 274 (issue #2523): the seven call sites (six list-page "replace"
// banners plus CameraDetailPage's "stale" banner) rendered this identical
// fragment inline. `message` alone carries the replace/stale distinction —
// no `variant` prop, because nothing about the DOM or behaviour differs
// between the two, only the sentence (plan.md §2, ADR-0036).
export function RetryBanner({ message, onRetry }: RetryBannerProps) {
  return (
    <div
      role="alert"
      className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
    >
      {message}{' '}
      <button type="button" className="underline" onClick={onRetry}>
        Retry
      </button>
    </div>
  );
}
