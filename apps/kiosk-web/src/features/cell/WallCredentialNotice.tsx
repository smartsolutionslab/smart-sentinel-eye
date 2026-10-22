import type { WallCredentialState } from './useWallCredential.js';

export interface WallCredentialNoticeProps {
  state: Exclude<WallCredentialState, 'idle'>;
}

export function WallCredentialNotice(_props: WallCredentialNoticeProps) {
  return null;
}
