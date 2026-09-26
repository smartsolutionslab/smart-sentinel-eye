import { createApi } from '@reduxjs/toolkit/query/react';
import type { FetchArgs } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { CreateWallInput } from './walls.schema.js';

export type { CreateWallInput };

/**
 * Spec 258 PD-2. `Showing` is a `LayoutIdentifier`, resolved at display time
 * to that chain's current Published revision (PD-3) — never a pinned
 * revision. `sceneVersion` is the monotonic counter the kiosk uses to
 * discard out-of-order `WallSceneChanged` frames (FR-008).
 */
export interface Wall {
  wallIdentifier: string;
  /** Optimistic-concurrency version; echo it back via If-Match to mutate (ADR-0113). */
  version: number;
  fab: string;
  name: string;
  scenes: string[];
  showing: string;
  sceneVersion: number;
  showingSince: string;
}

export interface SwitchWallSceneInput {
  wallIdentifier: string;
  /** The wall's current version, echoed as If-Match (ADR-0113). */
  version: number;
  target: 'next' | 'layout';
  /** Required iff `target === 'layout'` (US1-4). */
  layout?: string;
}

export interface EditWallScenesInput {
  wallIdentifier: string;
  version: number;
  scenes: string[];
}

// Named so each can be re-attached to its endpoint below (walls.api.test.ts,
// T014): RTK Query's built endpoint object does not re-expose the `query`
// function a mutation was defined with, so a unit test that wants to assert
// on the request shape without a store or a fetch mock needs it handed back
// explicitly. The endpoint's `query` field below is this same reference —
// not a second implementation free to drift.
function switchWallSceneQuery({ wallIdentifier, version, target, layout }: SwitchWallSceneInput): FetchArgs {
  return {
    url: `/${wallIdentifier}/switch`,
    method: 'POST',
    headers: ifMatch(version),
    body: layout === undefined ? { target } : { target, layout },
  };
}

function editWallScenesQuery({ wallIdentifier, version, scenes }: EditWallScenesInput): FetchArgs {
  return {
    url: `/${wallIdentifier}/scenes`,
    method: 'PUT',
    headers: ifMatch(version),
    body: { scenes },
  };
}

/**
 * Gateway route `layout-composition/walls` (plan.md §4.5, tasks.md T002):
 * there is no bare `/walls` route — mirrors exactly how `layouts.api.ts`
 * calls `gatewayBaseQuery('layout-composition/layouts')`.
 */
const wallsApiBase = createApi({
  reducerPath: 'wallsApi',
  baseQuery: gatewayBaseQuery('layout-composition/walls'),
  tagTypes: ['Wall', 'WallList'],
  endpoints: (build) => ({
    createWall: build.mutation<string, CreateWallInput>({
      query: (body) => ({
        url: '',
        method: 'POST',
        body: { name: body.name, scenes: body.scenes },
      }),
      invalidatesTags: [{ type: 'WallList', id: 'ALL' }],
    }),
    getWall: build.query<Wall, string>({
      query: (wallIdentifier) => `/${wallIdentifier}`,
      providesTags: (_result, _error, wallIdentifier) => [{ type: 'Wall', id: wallIdentifier }],
    }),
    listWalls: build.query<Wall[], void>({
      query: () => '',
      providesTags: () => [{ type: 'WallList', id: 'ALL' }],
    }),
    editWallScenes: build.mutation<Wall, EditWallScenesInput>({
      query: editWallScenesQuery,
      invalidatesTags: (_r, _e, { wallIdentifier }) => [
        { type: 'Wall', id: wallIdentifier },
        { type: 'WallList', id: 'ALL' },
      ],
    }),
    switchWallScene: build.mutation<Wall, SwitchWallSceneInput>({
      query: switchWallSceneQuery,
      invalidatesTags: (_r, _e, { wallIdentifier }) => [{ type: 'Wall', id: wallIdentifier }],
    }),
  }),
});

// See the comment on the two functions above: re-attached here so a unit
// test can call `wallsApi.endpoints.<name>.query(input)` directly, the way
// `walls.api.test.ts` does, without a store or a fetch mock. The cast below
// makes that shape visible to the type checker too — RTK Query's own
// `ApiEndpointMutation` type does not declare `query`, since nothing else in
// this codebase reaches for it this way.
Object.assign(wallsApiBase.endpoints.switchWallScene, { query: switchWallSceneQuery });
Object.assign(wallsApiBase.endpoints.editWallScenes, { query: editWallScenesQuery });

export const wallsApi = wallsApiBase as typeof wallsApiBase & {
  endpoints: {
    switchWallScene: (typeof wallsApiBase.endpoints)['switchWallScene'] & { query: typeof switchWallSceneQuery };
    editWallScenes: (typeof wallsApiBase.endpoints)['editWallScenes'] & { query: typeof editWallScenesQuery };
  };
};

export const {
  useCreateWallMutation,
  useGetWallQuery,
  useListWallsQuery,
  useEditWallScenesMutation,
  useSwitchWallSceneMutation,
} = wallsApi;
