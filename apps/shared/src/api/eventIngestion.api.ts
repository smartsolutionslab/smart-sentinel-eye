import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery } from './gateway.js';

/**
 * Issue #2780 — the shape `GET /events/dead-letters` returns
 * (`EventsEndpoints.Reads.cs`, `DeadLetterDto`) — the service-internal route;
 * through the gateway (ADR-0106) this app requests
 * `/event-ingestion/events/dead-letters`, which the gateway strips back down
 * to the service's own `/events/dead-letters` before forwarding. `Fab` and
 * `Kind` are nullable, mirroring the aggregate's own nullability (spec 018
 * FR-010; spec 317 FR-002) — a row can be fab-neutral or, for a
 * `ParseFailure` row, have no derivable kind at all. This remote only ever
 * requests `reason=UnknownEventType&state=Held`, so in practice every row it
 * receives carries both.
 */
export interface DeadLetter {
  deadLetterIdentifier: string;
  topic: string;
  rawPayload: string;
  error: string;
  rejectedAt: string;
  fab: string | null;
  reason: string;
  kind: string | null;
  state: string;
}

export interface ListHeldEventTypesFilters {
  /** Omit to span every fab the caller holds; name one to narrow to it. */
  fabId?: string;
}

// `ListDeadLettersQueryHandler.MaximumLimit` (`EventsEndpoints.Reads.cs`) —
// the server's stated ceiling for `?limit=`. Sent explicitly rather than
// relying on the server's own 100-row default: the server orders newest
// first, so a kind whose held rows are all older than the newest 100 would
// otherwise drop out of the grouped list entirely, and the group's
// count/first-seen would describe only that partial window while read as a
// total. `HeldEventTypesPage` compares the response length against this same
// constant to show a visible "may be partial" notice rather than presenting
// a truncated page as complete.
export const HELD_EVENT_TYPES_LIMIT = 1000;

export const eventIngestionApi = createApi({
  reducerPath: 'eventIngestionApi',
  baseQuery: gatewayBaseQuery('event-ingestion/events'),
  tagTypes: ['HeldEventType'],
  endpoints: (build) => ({
    // Fixed reason=UnknownEventType&state=Held: this is the one listing
    // this remote's US1 shows (issue #2780) — a discovery source's held,
    // unregistered-type rows, not the full dead-letter table (ParseFailure
    // and Refused rows are a different operator concern, out of scope here).
    listHeldEventTypes: build.query<DeadLetter[], ListHeldEventTypesFilters | undefined>({
      query: (filters) => ({
        url: '/dead-letters',
        method: 'GET',
        params: {
          reason: 'UnknownEventType',
          state: 'Held',
          limit: HELD_EVENT_TYPES_LIMIT,
          ...(filters?.fabId ? { fabId: filters.fabId } : {}),
        },
      }),
      providesTags: () => [{ type: 'HeldEventType', id: 'ALL' }],
    }),
  }),
});

export const { useListHeldEventTypesQuery } = eventIngestionApi;
