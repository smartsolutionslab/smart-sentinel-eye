// ADR-0074 / ADR-0106 (#1005): the API gateway origin is injected at runtime —
// Aspire sets VITE_API_GATEWAY_URL in dev, the deploy layer supplies the public
// gateway URL in prod. Declared here because the workspace does not pull in the
// `vite/client` ambient types.
interface ImportMetaEnv {
  readonly VITE_API_GATEWAY_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
