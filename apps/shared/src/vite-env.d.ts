// ADR-0074 / ADR-0106 (#1005): runtime-injected origins. Aspire sets these in
// dev (VITE_API_GATEWAY_URL = the gateway proxy; VITE_KEYCLOAK_URL = the Keycloak
// host endpoint, which must match the issuer the services validate against); the
// deploy layer supplies the public URLs in prod. Declared here because the
// workspace does not pull in the `vite/client` ambient types.
interface ImportMetaEnv {
  readonly PROD: boolean;
  readonly VITE_API_GATEWAY_URL?: string;
  readonly VITE_KEYCLOAK_URL?: string;
  readonly VITE_LAYOUT_HUB_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
