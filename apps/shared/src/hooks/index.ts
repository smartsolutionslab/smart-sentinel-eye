// Cross-app React hooks land here.
export { useDebouncedValue, DEBOUNCE_MS } from './useDebouncedValue.js';
export {
  useRevocationFallback,
  REVOCATION_STRIKE_THRESHOLD,
  type RevocationQueryState,
  type RevocationSource,
} from './useRevocationFallback.js';
export { useAssignedFabs } from './useAssignedFabs.js';
export { useClearOnClose, useResetOnOpen, useSubmitGuard } from './useFormDialog.js';
// useCameraStream is imported by its own subpath: it pulls in streams.api, hence gateway.ts, which reads VITE_API_GATEWAY_URL at module load — a test that imports DEBOUNCE_MS from here would freeze the origin before it can stub it.
