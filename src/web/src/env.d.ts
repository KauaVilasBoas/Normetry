interface ImportMetaEnv {
  /** Keycloak realm URL; the AppHost sets it. Defaults to the local dev realm. */
  readonly VITE_OIDC_AUTHORITY?: string;
}
