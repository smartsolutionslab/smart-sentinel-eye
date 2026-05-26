import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

export type StreamState = 'Provisioning' | 'Healthy' | 'Degraded' | 'Offline';

export type TranscodeMode = 'Passthrough' | 'Software' | 'Unknown';

export interface StreamHealth {
  cameraIdentifier: string;
  state: StreamState;
  whepUrl: string;
  transcodeMode: TranscodeMode;
  lastSuccessAt: string | null;
  error: string | null;
}

export const streamsApi = createApi({
  reducerPath: 'streamsApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/streams' }),
  tagTypes: ['Stream'],
  endpoints: (build) => ({
    getStream: build.query<StreamHealth, string>({
      query: (cameraIdentifier) => `/${cameraIdentifier}`,
      providesTags: (_result, _error, cameraIdentifier) => [
        { type: 'Stream' as const, id: cameraIdentifier },
      ],
    }),
    listStreams: build.query<StreamHealth[], string[]>({
      query: (cameraIdentifiers) => ({
        url: '',
        method: 'GET',
        params:
          cameraIdentifiers.length === 0
            ? undefined
            : { cameraIdentifiers: cameraIdentifiers.join(',') },
      }),
      providesTags: (result) =>
        result
          ? [
              ...result.map((stream) => ({
                type: 'Stream' as const,
                id: stream.cameraIdentifier,
              })),
              { type: 'Stream' as const, id: 'LIST' },
            ]
          : [{ type: 'Stream' as const, id: 'LIST' }],
    }),
  }),
});

export const { useGetStreamQuery, useListStreamsQuery } = streamsApi;
