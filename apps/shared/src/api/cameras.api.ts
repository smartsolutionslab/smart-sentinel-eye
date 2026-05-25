import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import type { RegisterCameraInput } from './cameras.schema.js';

export type RegisterCameraResponse = string;

export const camerasApi = createApi({
  reducerPath: 'camerasApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/cameras' }),
  tagTypes: ['Camera'],
  endpoints: (build) => ({
    registerCamera: build.mutation<RegisterCameraResponse, RegisterCameraInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
      invalidatesTags: ['Camera'],
    }),
  }),
});

export const { useRegisterCameraMutation } = camerasApi;
