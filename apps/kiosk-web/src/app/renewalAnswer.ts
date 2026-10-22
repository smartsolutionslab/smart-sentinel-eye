let answeredWithoutCredential = false;

/**
 * Records whether the identity provider's last answer to a silent renewal was
 * a user holding no access token (spec 205 A2). It is the one positive sign of
 * a refusal the wall can read: a rejection (provider unreachable) and the
 * library's swallowed failure (a null user, classified a commit later from
 * `auth.error`) both keep the existing retry or the not-authorized screen in
 * charge, and neither is evidence that this display was refused.
 */
export const recordRenewalAnswer = (withoutCredential: boolean): void => {
  answeredWithoutCredential = withoutCredential;
};

export const renewalAnsweredWithoutCredential = (): boolean => answeredWithoutCredential;
