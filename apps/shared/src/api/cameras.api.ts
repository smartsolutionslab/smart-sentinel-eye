import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import type { RegisterCameraInput } from './cameras.schema.js';

export type RegisterCameraResponse = string;

export type CameraSortField = 'name' | 'registeredAt';
export type CameraSortOrder = 'asc' | 'desc';

export interface ListCamerasParams {
  sort?: CameraSortField;
  order?: CameraSortOrder;
  offset?: number;
  limit?: number;
}

export interface CameraSummary {
  cameraIdentifier: string;
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
  baseQuery: fetchBaseQuery({ baseUrl: '/api/cameras' }),
  tagTypes: ['Camera'],
  endpoints: (build) => ({
    registerCamera: build.mutation<RegisterCameraResponse, RegisterCameraInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
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
