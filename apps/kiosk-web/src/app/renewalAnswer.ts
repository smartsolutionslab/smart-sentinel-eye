/**
 * Whether the identity provider's last answer to a silent renewal was a
 * user with no access token. Stub: behaviourless until the fix lands.
 */
export const recordRenewalAnswer = (_answeredWithoutCredential: boolean): void => undefined;

export const renewalAnsweredWithoutCredential = (): boolean => false;
