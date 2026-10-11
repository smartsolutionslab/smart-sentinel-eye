import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery } from './gateway.js';

/**
 * Issue #2855 — promotes a held event type (spec 018, decision 018) into the
 * registry, closing the dead-letter hold a `discovery`-mode source left
 * behind. `POST /event-types` (`EventTypesEndpoints.cs`) is a sibling route
 * group to `/events`, not nested under it — a separate api slice from
 * `eventIngestion.api.ts` because the two groups have different base paths
 * through the gateway (ADR-0106): `/event-ingestion/event-types`, not
 * `/event-ingestion/events/...`.
 */
export interface PromoteEventTypeInput {
  /** Taken from the held group's own `fab` field, never re-derived — the
   * endpoint resolves the fab from this query param, and ADR-0114 requires
   * naming it explicitly rather than letting a multi-fab caller be guessed
   * for. */
  fab: string;
  kind: string;
}

export const eventTypesApi = createApi({
  reducerPath: 'eventTypesApi',
  baseQuery: gatewayBaseQuery('event-ingestion/event-types'),
  endpoints: (build) => ({
    promoteEventType: build.mutation<string, PromoteEventTypeInput>({
      // A fresh key per call (ADR-0142): each Promote click is its own
      // idempotency scope, not a retry of a prior one.
      query: ({ fab, kind }) => ({
        url: '',
        method: 'POST',
        params: { fabId: fab },
        headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: { kind },
      }),
    }),
  }),
});

export const { usePromoteEventTypeMutation } = eventTypesApi;
