import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

/** One audit row as returned by the AuditObservability read API (spec 009 FR-008/010). */
export interface AuditRow {
  auditIdentifier: string;
  occurredAt: string;
  receivedAt: string;
  /** Owning fab, or `null` for cross-fab rows. */
  fab: string | null;
  eventKind: string;
  resourceKind: string | null;
  resourceIdentifier: string | null;
  actorIdentifier: string;
  actorIsSystem: boolean;
  actorUsername: string | null;
  eventIdentifier: string;
  /** Verbatim V1 JSON payload. */
  payload: string;
  payloadSizeBytes: number;
  schemaVersion: number;
}

/** Cursor-paginated page of audit rows. `nextCursor` is `null` at the end. */
export interface AuditPage {
  rows: AuditRow[];
  nextCursor: string | null;
}

/** Cross-cutting search filters (keys match the GET /audit query params). */
export interface SearchAuditInput {
  fabId?: string;
  actor?: string;
  actorUsername?: string;
  eventKind?: string;
  resourceKind?: string;
  resourceIdentifier?: string;
  since?: string;
  until?: string;
  pageSize?: number;
  cursor?: string;
}

export interface ResourceTimelineInput {
  resourceKind: string;
  resourceIdentifier: string;
  fabId: string;
  since?: string;
  until?: string;
  pageSize?: number;
  cursor?: string;
}

/** Drop unset / empty filters so they don't reach the wire as blank query params. */
function definedParams(input: Record<string, string | number | undefined>): Record<string, string | number> {
  const out: Record<string, string | number> = {};
  for (const [key, value] of Object.entries(input)) {
    if (value !== undefined && value !== '') {
      out[key] = value;
    }
  }
  return out;
}

export const auditApi = createApi({
  reducerPath: 'auditApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/audit' }),
  tagTypes: ['AuditPage', 'AuditEvent'],
  endpoints: (build) => ({
    searchAudit: build.query<AuditPage, SearchAuditInput>({
      query: (input) => ({ url: '', method: 'GET', params: definedParams({ ...input }) }),
      providesTags: () => [{ type: 'AuditPage', id: 'SEARCH' }],
    }),
    getResourceTimeline: build.query<AuditPage, ResourceTimelineInput>({
      query: ({ resourceKind, resourceIdentifier, ...rest }) => ({
        url: `/${encodeURIComponent(resourceKind)}/${encodeURIComponent(resourceIdentifier)}`,
        method: 'GET',
        params: definedParams({ ...rest }),
      }),
      providesTags: (_r, _e, { resourceKind, resourceIdentifier }) => [
        { type: 'AuditPage', id: `${resourceKind}/${resourceIdentifier}` },
      ],
    }),
    getAuditEvent: build.query<AuditRow, string>({
      query: (auditIdentifier) => `/${encodeURIComponent(auditIdentifier)}`,
      providesTags: (_r, _e, id) => [{ type: 'AuditEvent', id }],
    }),
  }),
});

export const { useSearchAuditQuery, useGetResourceTimelineQuery, useGetAuditEventQuery } = auditApi;
