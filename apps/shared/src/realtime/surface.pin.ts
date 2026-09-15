// Pin for RealtimeClient and RealtimeMessage (apps/shared/src/realtime/index.ts).
// Kept in its own file, deliberately: spec 162 §3.4's discrimination criterion
// asks "can the assertion's subject change without the assertion's text
// changing?", and that only holds when the subject (`RealtimeClient` /
// `RealtimeMessage`) and the expectation (the literal member lists below)
// live in different files. Import the types; never redeclare them here.
import type { RealtimeClient, RealtimeMessage } from './index.js';

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

// Same technique, same property, applied to the envelope shape rather than
// the client's method surface. `client.spec.ts` (deleted — never collected in
// 15 months, #2397) asserted a literal satisfies `RealtimeMessage`; that was
// type-level and genuinely typechecked regardless of vitest collection, so
// deleting the file removed a real, if weak, check on this type. This pin
// replaces it in the same style as RealtimeClientSurfaceIsExhaustive above:
// the subject (`RealtimeMessage`) stays in index.ts, the expectation (the
// literal field list) lives here, and every field of RealtimeMessage is
// required with no index signature, so `keyof` set-equality pins it
// exhaustively in both directions — adding a field or removing one both fail
// `tsc`, verified by counterfactual (#2397 review finding 6).
export type RealtimeMessageSurfaceIsExhaustive = Assert<
  Exact<keyof RealtimeMessage, 'type' | 'payload' | 'traceId' | 'ts'>
>;
