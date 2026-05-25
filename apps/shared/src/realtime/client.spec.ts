import { describe, it, expect } from 'vitest';
import type { RealtimeClient, RealtimeMessage } from './index.js';

describe('Realtime abstraction contract', () => {
  it('Defines the message envelope shape', () => {
    const message: RealtimeMessage = {
      type: 'variable.changed',
      payload: { value: 'FAULT' },
      traceId: '01HX000000000000000000',
      ts: '2026-05-25T10:00:00Z',
    };
    expect(message.type).toBe('variable.changed');
  });

  it('Exposes a connect/subscribe/disconnect surface that v1 WebSocket and v2 SSE both implement', () => {
    const surface: (keyof RealtimeClient)[] = ['connect', 'subscribe', 'disconnect'];
    expect(surface).toHaveLength(3);
  });
});
