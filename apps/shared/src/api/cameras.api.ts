import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery } from './gateway.js';
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
   * The fab this camera belongs to (spec 015). A camera never arrives here
   * unless the caller is assigned to its fab, so this is always one of theirs.
   * On the wire because a multi-fab operator's listing can hold two rows of the
   * same name with nothing else to tell them apart.
   */
  fab: string;
  name: string;
  rtspUrl: string;
  registeredAt: string;
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

export const { useRegisterCameraMutation, useListCamerasQuery } = camerasApi;
