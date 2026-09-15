// Pin for RealtimeClient (apps/shared/src/realtime/index.ts). Kept in its
// own file, deliberately: spec 162 §3.4's discrimination criterion asks
// "can the assertion's subject change without the assertion's text
// changing?", and that only holds when the subject (`RealtimeClient`) and
// the expectation (the literal member list below) live in different files.
// Import the interface; never redeclare it here.
import type { RealtimeClient } from './index.js';

type Exact<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
type Assert<T extends true> = T;

// ADR-0076 promises a v2 transport drops in behind RealtimeClient without
// changing consumers. Adding or removing a member breaks that promise, so
// the member set is pinned here: tsc fails in BOTH directions, which a
// `keyof`-typed array does not (a subset satisfies it). See spec 162 §3.3.
//
// Only the member *names* are pinned — `keyof` sees names, not signatures.
// Changing `connect(): Promise<void>` to `connect(retries: number): void`
// typechecks clean here; a v2 transport that changed a handler signature
// would not be caught by this pin.
//
// This is a type-only assertion, not a `const` value binding, so the module
// keeps erasing to nothing (see index.ts's note on erasure). The tradeoff is
// a less legible failure — `TS2344` (constraint not satisfied) rather than
// the `const` form's `TS2322` (value not assignable) — accepted rather than
// trading away the erasure property.
export type RealtimeClientSurfaceIsExhaustive = Assert<
  Exact<keyof RealtimeClient, 'connect' | 'subscribe' | 'disconnect'>
>;
