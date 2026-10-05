import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { CreateOverlayDraftInput } from './overlays.schema.js';

export type { CreateOverlayDraftInput };

export type OverlayRevisionState = 'Draft' | 'Published' | 'Archived';

/** The Text variant's wire shape — `kind`/`color` mandatory, the way every real element the API returns carries both. */
export interface OverlayTextElement {
  kind: 'Text';
  color: string;
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}

/** A Box or Ellipse stroke — the non-text variants of {@link OverlayElement} (ADR-0165). */
export interface OverlayShape {
  kind: 'Box' | 'Ellipse';
  color: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
}

/**
 * One overlay primitive — a text label, a box, or an ellipse (spec 300,
 * #2349, ADR-0165). `OverlayTextElement | OverlayShape`, both with a
 * mandatory `kind`, so code consuming a real element set (`LayoutGrid.tsx`,
 * `CameraViewer.tsx`) discriminates exhaustively.
 */
export type OverlayElement = OverlayTextElement | OverlayShape;

/**
 * The shape `OverlayEditor.tsx`'s `value` prop expects — kept separate from
 * {@link OverlayTextElement}, with `kind` and `color` optional, because that
 * component is also exercised standalone with fixtures that set neither, by
 * guards this spec must leave byte-identical (FR-016). An absent `color` is
 * treated the same as {@link DEFAULT_OVERLAY_COLOR} by every reader
 * (`overlayLabelSurfaceStyle`). Every `OverlayTextElement` already satisfies
 * this structurally (required fields satisfy optional ones), so no cast is
 * needed handing a real element to the editor — only the reverse direction
 * (editor output back onto the wire-typed array) needs one.
 */
export interface OverlayLabel {
  kind?: 'Text';
  color?: string;
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}

export interface OverlayRevision {
  revisionIdentifier: string;
  revisionNumber: number;
  state: OverlayRevisionState;
  /** Ordered, non-empty set of 1..8 elements, any mix of kinds (spec 300, ADR-0165). Paint order is array order. */
  elements: OverlayElement[];
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
  /** Ordered, non-empty set of 1..8 elements, any mix of kinds (spec 300, ADR-0165). Paint order is array order. */
  elements: OverlayElement[];
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
      providesTags: (_result, _error, overlayIdentifier) => [{ type: 'Overlay', id: overlayIdentifier }],
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
    editDraftOverlayRevision: build.mutation<number, OverlayRevisionRouteInput & { elements: OverlayElement[] }>({
      query: ({ overlayIdentifier, revisionNumber, version, elements }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}`,
        method: 'PATCH',
        headers: ifMatch(version),
        body: { elements },
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
