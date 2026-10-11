import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery } from './gateway.js';

/**
 * Issue #2780 — the shape `GET /events/dead-letters` returns
 * (`EventsEndpoints.Reads.cs`, `DeadLetterDto`). `Fab` and `Kind` are
 * nullable, mirroring the aggregate's own nullability (spec 018 FR-010;
 * spec 317 FR-002) — a row can be fab-neutral or, for a `ParseFailure`
 * row, have no derivable kind at all. This remote only ever requests
 * `reason=UnknownEventType&state=Held`, so in practice every row it
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
          ...(filters?.fabId ? { fabId: filters.fabId } : {}),
        },
      }),
      providesTags: () => [{ type: 'HeldEventType', id: 'ALL' }],
    }),
  }),
});

export const { useListHeldEventTypesQuery } = eventIngestionApi;
