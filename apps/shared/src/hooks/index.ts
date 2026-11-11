// Cross-app React hooks land here.
export { useDebouncedValue, DEBOUNCE_MS } from './useDebouncedValue';
export {
  useRevocationFallback,
  REVOCATION_STRIKE_THRESHOLD,
  type RevocationQueryState,
  type RevocationSource,
} from './useRevocationFallback';
export { useAssignedFabs } from './useAssignedFabs';
export { useLatestAccessToken } from './useLatestAccessToken';
export { useClearOnClose, useResetOnOpen, useSubmitGuard } from './useFormDialog';
// useCameraStream is imported by its own subpath: it pulls in streams.api, hence gateway.ts, which reads VITE_API_GATEWAY_URL at module load — a test that imports DEBOUNCE_MS from here would freeze the origin before it can stub it.
