export interface HubUrlEnv {
  PROD: boolean;
  VITE_LAYOUT_HUB_URL?: string;
}

/**
 * Resolves the LayoutLifecycle hub endpoint (spec 011 FR-010, contracts §6).
 * Dev keeps the relative path served by the Vite `/hubs` proxy; production
 * builds MUST inject an absolute URL via VITE_LAYOUT_HUB_URL — a missing
 * value fails loudly at module load instead of 404ing at runtime.
 */
export function resolveLayoutHubUrl(env: HubUrlEnv): string {
  const configured = (env.VITE_LAYOUT_HUB_URL ?? '').replace(/\/+$/, '');
  if (configured !== '') {
    return configured;
  }
  if (env.PROD) {
    throw new Error(
      'VITE_LAYOUT_HUB_URL must be set in production builds — the dev-only Vite /hubs proxy does not exist there (see docs/deployment-frontend-env.md).',
    );
  }
  return '/hubs/layouts';
}

export const layoutHubUrl: string = resolveLayoutHubUrl(import.meta.env);
