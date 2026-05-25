// Cross-cutting TypeScript types shared between management-web and kiosk-web.
// Concrete types land per feature.

export type AccessToken = {
  readonly token: string;
  readonly expiresAt: Date;
};
