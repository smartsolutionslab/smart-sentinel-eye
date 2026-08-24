import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { RegisterCameraInput } from './cameras.schema.js';

export type RegisterCameraResponse = string;

export type CameraSortField = 'name' | 'registeredAt';
export type CameraSortOrder = 'asc' | 'desc';

export interface ListCamerasParams {
  /** Omit to span every fab the caller holds; name one to narrow to it (spec 015 FR-005). */
  fabId?: string;
  sort?: CameraSortField;
  order?: CameraSortOrder;
  offset?: number;
  limit?: number;
}

export interface CameraSummary {
  cameraIdentifier: string;
  /**
   * Optimistic-concurrency version (ADR-0113). On every row, not only on the
   * single-camera read, so a correction can be made straight from the listing
   * without a read-one round-trip — the reason spec 029 put it here.
   */
  version: number;
  /**
   * The fab this camera belongs to (spec 015). A camera never arrives here
   * unless the caller is assigned to its fab, so this is always one of theirs.
   * On the wire because a multi-fab operator's listing can hold two rows of the
   * same name with nothing else to tell them apart.
   */
  fab: string;
  name: string;
  rtspUrl: string;
  registeredAt: string;
  /** `Registered` or `Decommissioned` (spec 029 FR-007). */
  status: string;
}

/**
 * One camera, as `GET /cameras/{camera}` returns it (spec 029 FR-001).
 *
 * Structurally the same as {@link CameraSummary} today. Kept as its own type
 * rather than aliased: the two answer different questions, and a listing row is
 * free to diverge from a detail view without silently changing what a detail
 * page believes it has.
 */
export interface CameraDetail {
  cameraIdentifier: string;
  /** Echoed back via `If-Match` to correct the address (ADR-0113). */
  version: number;
  fab: string;
  name: string;
  rtspUrl: string;
  registeredAt: string;
  /**
   * `Registered` or `Decommissioned`. A retired camera is returned rather than
   * reported missing (spec 029 FR-002) — retirement takes a camera out of the
   * default listing, not out of existence.
   */
  status: string;
}

export interface ChangeCameraAddressInput {
  cameraIdentifier: string;
  rtspUrl: string;
  /** The version the operator was shown. Required — a blind write is refused 428. */
  version: number;
  fabId?: string;
}

export interface RetireCameraInput {
  cameraIdentifier: string;
  fabId?: string;
  /**
   * No `version`, deliberately. Retirement is idempotent rather than
   * version-checked (spec 028): the endpoint answers `204` whether or not the
   * camera was already retired, and declares no `409`, `412` or `428`. Sending
   * a precondition would invent a failure mode the server does not have — and
   * the detail page holds a version for the address correction, which is what
   * makes threading it in here the easy mistake.
   */
}

export interface CameraListPage {
  items: CameraSummary[];
  count: number;
  offset: number;
  limit: number;
}

export const camerasApi = createApi({
  reducerPath: 'camerasApi',
  baseQuery: gatewayBaseQuery('camera-catalog/cameras'),
  tagTypes: ['Camera'],
  endpoints: (build) => ({
    registerCamera: build.mutation<RegisterCameraResponse, RegisterCameraInput & { fabId?: string }>({
      // fabId travels as a query parameter, not in the body: an operator in one
      // fab has it inferred and is never asked (ADR-0114), and
      // registerCameraSchema mirrors the body alone.
      query: ({ fabId, ...body }) => ({
        url: '',
        method: 'POST',
        ...(fabId !== undefined && fabId !== '' ? { params: { fabId } } : {}),
        body,
      }),
      invalidatesTags: ['Camera'],
    }),
    getCamera: build.query<CameraDetail, { cameraIdentifier: string; fabId?: string }>({
      query: ({ cameraIdentifier, fabId }) => ({
        url: `/${cameraIdentifier}`,
        method: 'GET',
        ...(fabId !== undefined && fabId !== '' ? { params: { fabId } } : {}),
      }),
      providesTags: (_result, _error, { cameraIdentifier }) => [
        { type: 'Camera' as const, id: cameraIdentifier },
      ],
    }),
    changeCameraAddress: build.mutation<void, ChangeCameraAddressInput>({
      // The version travels as an If-Match header, not in the body, and is
      // threaded explicitly through the arguments rather than pulled from a
      // cache — gateway.ts records why: a miss would degrade to a request with
      // no version, which the server refuses 428 rather than silently accepting.
      query: ({ cameraIdentifier, rtspUrl, version, fabId }) => ({
        url: `/${cameraIdentifier}`,
        method: 'PATCH',
        headers: ifMatch(version),
        ...(fabId !== undefined && fabId !== '' ? { params: { fabId } } : {}),
        body: { rtspUrl },
      }),
      // Both: the camera itself, and the listing whose row now shows a stale
      // address and a stale version.
      invalidatesTags: (_result, _error, { cameraIdentifier }) => [
        { type: 'Camera' as const, id: cameraIdentifier },
        { type: 'Camera' as const, id: 'LIST' },
      ],
    }),
    retireCamera: build.mutation<void, RetireCameraInput>({
      // No headers at all: no If-Match, and no body. Contrast
      // changeCameraAddress directly above, which must carry one.
      query: ({ cameraIdentifier, fabId }) => ({
        url: `/${cameraIdentifier}/retire`,
        method: 'POST',
        ...(fabId !== undefined && fabId !== '' ? { params: { fabId } } : {}),
      }),
      // The same two as changeCameraAddress, and they do more work here than
      // the count suggests. Invalidation *refetches* for a mounted subscriber
      // rather than evicting, so the camera tag refreshes the detail page into
      // its retired state without a reload (FR-009) and the record stays
      // readable at its own address (FR-011) — the endpoint still serves
      // retired cameras. The LIST tag drops it from the listing, which excludes
      // retired cameras by default (FR-010).
      invalidatesTags: (_result, _error, { cameraIdentifier }) => [
        { type: 'Camera' as const, id: cameraIdentifier },
        { type: 'Camera' as const, id: 'LIST' },
      ],
    }),
    listCameras: build.query<CameraListPage, ListCamerasParams | void>({
      query: (params) => ({
        url: '',
        method: 'GET',
        params: params ?? undefined,
      }),
      providesTags: (result) =>
        result
          ? [
              ...result.items.map(({ cameraIdentifier }) => ({
                type: 'Camera' as const,
                id: cameraIdentifier,
              })),
              { type: 'Camera' as const, id: 'LIST' },
            ]
          : [{ type: 'Camera' as const, id: 'LIST' }],
    }),
  }),
});

export const {
  useRegisterCameraMutation,
  useListCamerasQuery,
  useGetCameraQuery,
  useChangeCameraAddressMutation,
  useRetireCameraMutation,
} = camerasApi;
