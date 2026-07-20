export type ResilienceSubsystem = 'stream' | 'hub' | 'session' | 'crash';

/**
 * Structured log line for every resilience state transition (spec 011
 * FR-017). The stable `[resilience]` prefix + shape is an observable
 * contract: Playwright asserts on it and kiosk remote-debug sessions
 * grep for it, so changing the format is a breaking change.
 */
export function logResilienceEvent(
  subsystem: ResilienceSubsystem,
  transition: string,
  detail?: Record<string, unknown>,
): void {
  console.info('[resilience]', { subsystem, transition, ...detail });
}
