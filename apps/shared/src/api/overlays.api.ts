import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { CreateOverlayDraftInput } from './overlays.schema.js';

export type { CreateOverlayDraftInput };

export type OverlayRevisionState = 'Draft' | 'Published' | 'Archived';

export interface OverlayLabel {
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}

export interface OverlayRevision extends OverlayLabel {
  revisionIdentifier: string;
  revisionNumber: number;
  state: OverlayRevisionState;
  createdAt: string;
  createdBy: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export interface Overlay {
  overlayIdentifier: string;
  /** Optimistic-concurrency version; echo it back via If-Match to mutate (ADR-0113). */
  version: number;
  name: string;
  createdAt: string;
  createdBy: string;
  revisions: OverlayRevision[];
}

export interface PublishedOverlay {
  overlayIdentifier: string;
  name: string;
  revisionNumber: number;
  text: string;
  publishedAt: string;
}

export interface ListOverlaysResponse {
  chains: Overlay[];
  published: PublishedOverlay[];
}

export interface OverlayRevisionRouteInput {
  overlayIdentifier: string;
  revisionNumber: number;
  /** The chain version this edit was built on (ADR-0113). */
  version: number;
}

export interface OverlayChainRouteInput {
  overlayIdentifier: string;
  version: number;
}

export const overlaysApi = createApi({
  reducerPath: 'overlaysApi',
  baseQuery: gatewayBaseQuery('overlay-designer/overlays'),
  tagTypes: ['Overlay', 'OverlayList'],
  endpoints: (build) => ({
    createOverlayDraft: build.mutation<string, CreateOverlayDraftInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
      invalidatesTags: [{ type: 'OverlayList', id: 'ALL' }],
    }),
    getOverlay: build.query<Overlay, string>({
      query: (overlayIdentifier) => `/${overlayIdentifier}`,
      providesTags: (_result, _error, overlayIdentifier) => [
        { type: 'Overlay', id: overlayIdentifier },
      ],
    }),
    listOverlays: build.query<ListOverlaysResponse, OverlayRevisionState | undefined>({
      query: (state) => ({
        url: '',
        method: 'GET',
        params: state === undefined ? undefined : { state },
      }),
      providesTags: () => [{ type: 'OverlayList', id: 'ALL' }],
    }),
    publishOverlayRevision: build.mutation<number, OverlayRevisionRouteInput>({
      query: ({ overlayIdentifier, revisionNumber, version }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}/publish`,
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
    archiveOverlayRevision: build.mutation<number, OverlayRevisionRouteInput>({
      query: ({ overlayIdentifier, revisionNumber, version }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}/archive`,
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
    branchDraftOverlayRevision: build.mutation<number, OverlayChainRouteInput>({
      query: ({ overlayIdentifier, version }) => ({
        url: `/${overlayIdentifier}/draft`,
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
    editDraftOverlayRevision: build.mutation<
      number,
      OverlayRevisionRouteInput & { label: OverlayLabel }
    >({
      query: ({ overlayIdentifier, revisionNumber, version, label }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}`,
        method: 'PATCH',
        headers: ifMatch(version),
        body: { label },
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
    revertOverlayRevision: build.mutation<number, OverlayRevisionRouteInput>({
      query: ({ overlayIdentifier, revisionNumber, version }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}/revert`,
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
  }),
});

export const {
  useCreateOverlayDraftMutation,
  useGetOverlayQuery,
  useListOverlaysQuery,
  usePublishOverlayRevisionMutation,
  useArchiveOverlayRevisionMutation,
  useBranchDraftOverlayRevisionMutation,
  useEditDraftOverlayRevisionMutation,
  useRevertOverlayRevisionMutation,
} = overlaysApi;
