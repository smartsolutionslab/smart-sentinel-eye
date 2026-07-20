import { afterEach, describe, expect, it, vi } from 'vitest';
import { logResilienceEvent } from './resilienceLog.js';

describe('logResilienceEvent', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('Logs with the stable [resilience] prefix', () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);

    logResilienceEvent('stream', 'live→reconnecting');

    expect(info).toHaveBeenCalledWith('[resilience]', {
      subsystem: 'stream',
      transition: 'live→reconnecting',
    });
  });

  it('Spreads structured detail fields into the payload', () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);

    logResilienceEvent('hub', 'degraded→connected', { attempt: 3 });

    expect(info).toHaveBeenCalledWith('[resilience]', {
      subsystem: 'hub',
      transition: 'degraded→connected',
      attempt: 3,
    });
  });
});
