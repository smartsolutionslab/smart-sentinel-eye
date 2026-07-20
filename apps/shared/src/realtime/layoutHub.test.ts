import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

interface RetryContextLike {
  previousRetryCount: number;
  elapsedMilliseconds: number;
  retryReason: Error;
}

interface RetryPolicyLike {
  nextRetryDelayInMilliseconds: (context: RetryContextLike) => number | null;
}

interface FakeHubConnectionLike {
  state: string;
  startCalls: number;
  startImpl: () => Promise<void>;
  closeCallback: ((error?: Error) => void) | undefined;
  reconnectingCallback: ((error?: Error) => void) | undefined;
  reconnectedCallback: ((connectionId?: string) => void) | undefined;
}

const fakes = vi.hoisted(() => ({
  urls: [] as string[],
  retryPolicies: [] as RetryPolicyLike[],
  connections: [] as FakeHubConnectionLike[],
}));

vi.mock('@microsoft/signalr', () => {
  class FakeHubConnection {
    state = 'Disconnected';
    startCalls = 0;
    startImpl: () => Promise<void> = () => Promise.resolve();
    closeCallback: ((error?: Error) => void) | undefined;
    reconnectingCallback: ((error?: Error) => void) | undefined;
    reconnectedCallback: ((connectionId?: string) => void) | undefined;

    on(): void {}

    onclose(callback: (error?: Error) => void): void {
      this.closeCallback = callback;
    }

    onreconnecting(callback: (error?: Error) => void): void {
      this.reconnectingCallback = callback;
    }

    onreconnected(callback: (connectionId?: string) => void): void {
      this.reconnectedCallback = callback;
    }

    async start(): Promise<void> {
      this.startCalls += 1;
      await this.startImpl();
      this.state = 'Connected';
    }

    async stop(): Promise<void> {
      this.state = 'Disconnected';
    }
  }

  class HubConnectionBuilder {
    withUrl(url: string): this {
      fakes.urls.push(url);
      return this;
    }

    withAutomaticReconnect(policy: RetryPolicyLike): this {
      fakes.retryPolicies.push(policy);
      return this;
    }

    build(): FakeHubConnection {
      const connection = new FakeHubConnection();
      fakes.connections.push(connection);
      return connection;
    }
  }

  return {
    HubConnectionBuilder,
    HubConnectionState: {
      Disconnected: 'Disconnected',
      Connecting: 'Connecting',
      Connected: 'Connected',
      Disconnecting: 'Disconnecting',
      Reconnecting: 'Reconnecting',
    },
  };
});

import { createLayoutHubClient, type LayoutHubConnectionState } from './layoutHub.js';

function createClient(callbacks?: {
  onStateChange?: (state: LayoutHubConnectionState) => void;
  onReconnected?: () => void;
}) {
  return createLayoutHubClient({ accessTokenFactory: () => 'token' }, { ...callbacks });
}

function lastConnection(): FakeHubConnectionLike {
  return fakes.connections[fakes.connections.length - 1]!;
}

function lastPolicy(): RetryPolicyLike {
  return fakes.retryPolicies[fakes.retryPolicies.length - 1]!;
}

function retryDelay(policy: RetryPolicyLike, previousRetryCount: number): number | null {
  return policy.nextRetryDelayInMilliseconds({
    previousRetryCount,
    elapsedMilliseconds: 0,
    retryReason: new Error('transport lost'),
  });
}

describe('layout hub resilience (spec 011 FR-006/007)', () => {
  beforeEach(() => {
    fakes.urls.length = 0;
    fakes.retryPolicies.length = 0;
    fakes.connections.length = 0;
    vi.useFakeTimers();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Never returns null from the reconnect retry policy', () => {
    createClient();
    const policy = lastPolicy();

    for (let previousRetryCount = 0; previousRetryCount < 50; previousRetryCount += 1) {
      expect(retryDelay(policy, previousRetryCount)).not.toBeNull();
    }
  });

  it('Follows the 0/2/5/10/30s ladder with ±20% jitter, then stays at 30s', () => {
    createClient();
    const policy = lastPolicy();
    const bounds: [number, number, number][] = [
      [0, 0, 0],
      [1, 1_600, 2_400],
      [2, 4_000, 6_000],
      [3, 8_000, 12_000],
      [4, 24_000, 36_000],
      [17, 24_000, 36_000],
    ];

    for (const [previousRetryCount, min, max] of bounds) {
      for (let sample = 0; sample < 10; sample += 1) {
        const delay = retryDelay(policy, previousRetryCount);
        expect(delay).toBeGreaterThanOrEqual(min);
        expect(delay).toBeLessThanOrEqual(max);
      }
    }
  });

  it('Connects to the resolved default hub URL and honours a per-client override', () => {
    createClient();
    expect(fakes.urls[0]).toBe('/hubs/layouts');

    createLayoutHubClient(
      { hubUrl: 'https://fab.example/hubs/layouts', accessTokenFactory: () => 'token' },
      {},
    );
    expect(fakes.urls[1]).toBe('https://fab.example/hubs/layouts');
  });

  it('Schedules a restart when the server closes the connection and reconciles on success', async () => {
    const onReconnected = vi.fn();
    const handle = createClient({ onReconnected });
    await handle.start();
    const connection = lastConnection();
    expect(connection.startCalls).toBe(1);

    connection.closeCallback?.(new Error('server closed'));
    await vi.advanceTimersByTimeAsync(0);

    expect(connection.startCalls).toBe(2);
    expect(onReconnected).toHaveBeenCalledTimes(1);
  });

  it('Keeps retrying initial connect failures on the ladder until the hub is reachable', async () => {
    const states: LayoutHubConnectionState[] = [];
    const handle = createClient({ onStateChange: (state) => states.push(state) });
    const connection = lastConnection();
    connection.startImpl = () => Promise.reject(new Error('backend down'));

    await handle.start();
    expect(connection.startCalls).toBe(1);

    // First retry fires immediately (ladder index 0 = 0 ms).
    await vi.advanceTimersByTimeAsync(0);
    expect(connection.startCalls).toBe(2);

    // Second retry obeys the 2 s ±20% rung: nothing before 1.6 s…
    await vi.advanceTimersByTimeAsync(1_599);
    expect(connection.startCalls).toBe(2);
    // …but it lands by 2.4 s.
    await vi.advanceTimersByTimeAsync(801);
    expect(connection.startCalls).toBe(3);

    // Backend comes back: the next rung (5 s ±20%) connects.
    connection.startImpl = () => Promise.resolve();
    await vi.advanceTimersByTimeAsync(6_000);
    expect(connection.startCalls).toBe(4);
    expect(states[states.length - 1]).toBe('connected');
  });

  it('stop() cancels pending retries and prevents further starts', async () => {
    const handle = createClient();
    const connection = lastConnection();
    connection.startImpl = () => Promise.reject(new Error('backend down'));

    await handle.start();
    expect(connection.startCalls).toBe(1);

    await handle.stop();
    // Neither the pending retry timer nor a late server close may restart.
    connection.closeCallback?.(new Error('closed after stop'));
    await vi.advanceTimersByTimeAsync(120_000);

    expect(connection.startCalls).toBe(1);
  });

  it('Emits connecting→connected→degraded→connected across connect, drop, reconnect', async () => {
    const states: LayoutHubConnectionState[] = [];
    const handle = createClient({ onStateChange: (state) => states.push(state) });

    await handle.start();
    const connection = lastConnection();
    connection.reconnectingCallback?.(new Error('transport lost'));
    connection.reconnectedCallback?.('connection-2');

    expect(states).toEqual(['connecting', 'connected', 'degraded', 'connected']);
  });
});
