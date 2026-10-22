export type WallCredentialState = 'idle' | 'renewing' | 'refused';

export interface WallCredential {
  state: WallCredentialState;
  credentialGeneration: number;
  reportRefusal: (key: string, kind: 'unauthorized' | 'forbidden') => void;
}

export const useWallCredential = (_tileKeys: readonly string[], _accessToken: string | undefined): WallCredential => ({
  state: 'idle',
  credentialGeneration: 0,
  reportRefusal: () => undefined,
});
