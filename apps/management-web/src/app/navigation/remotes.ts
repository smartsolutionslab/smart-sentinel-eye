// Plan 316 §1/§4.4 — the shell's remote registry: one entry per federated
// remote, with the base URL Aspire injects (VITE_CAMERAS_REMOTE_URL,
// AppHost.cs). Mirrors `../auth.ts`'s VITE_KEYCLOAK_URL check line for line:
// a prod bundle with no remote URL would try to fetch remoteEntry.js from
// localhost on the operator's machine — fail loudly at module load instead,
// naming the variable (spec 011 FR-010).
const CAMERAS_REMOTE_BASE_URL = (import.meta.env.VITE_CAMERAS_REMOTE_URL ?? '').replace(/\/+$/, '');

if (import.meta.env.PROD && CAMERAS_REMOTE_BASE_URL === '') {
  throw new Error('VITE_CAMERAS_REMOTE_URL must be set in production builds (see docs/deployment-frontend-env.md).');
}

// Issue #2780 — the second remote. Same shape, same guard.
const EVENT_INGESTION_REMOTE_BASE_URL = (import.meta.env.VITE_EVENT_INGESTION_REMOTE_URL ?? '').replace(/\/+$/, '');

if (import.meta.env.PROD && EVENT_INGESTION_REMOTE_BASE_URL === '') {
  throw new Error(
    'VITE_EVENT_INGESTION_REMOTE_URL must be set in production builds (see docs/deployment-frontend-env.md).',
  );
}

export interface RemoteDescriptor {
  readonly name: string;
  readonly baseUrl: string;
}

export const remotes: readonly RemoteDescriptor[] = [
  { name: 'cameras', baseUrl: CAMERAS_REMOTE_BASE_URL },
  { name: 'eventIngestion', baseUrl: EVENT_INGESTION_REMOTE_BASE_URL },
];
