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

export interface RenameCameraInput {
  cameraIdentifier: string;
  /**
   * The corrected name, **exactly as the operator typed it**.
   *
   * Not case-normalised, ever. `Line-4-Inlet` and `line-4-inlet` normalise
   * identically — which is right for uniqueness and wrong for deciding whether
   * anything changed. Spec 033 found that trap in the repository predicate, the
   * aggregate's idempotency guard and EF's change tracker; lower-casing here
   * would make this the fourth, and the symptom is a rename that reports
   * success and changes nothing.
   */
  name: string;
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

/**
 * Every camera an operator may put on a tile, gathered across as many pages as
 * it takes (spec 048).
 *
 * <p>
 * <b>`complete` is carried rather than left to each consumer to work out.</b>
 * Today it is exactly `items.length >= count`, and nothing is hidden from a
 * caller that wanted to recompute it. It is carried because the rule belongs to
 * the producer: a consumer that re-derives it pins the rule at every call site,
 * and they drift apart the day the producer gains another reason to stop short.
 * </p>
 */
export interface CameraChoices {
  items: CameraSummary[];
  /**
   * How many cameras the operator could choose from, as reported by the source.
   * <b>Not `items.length`</b> — the gap between the two is the whole point, and
   * what the picker tells the operator about.
   */
  count: number;
  /** Whether `items` is all of them. */
  complete: boolean;
}

/**
 * The largest page the camera source will serve. It <b>refuses</b> anything
 * larger rather than clamping, so asking for more is an error, not a bigger
 * page.
 */
const MAXIMUM_PAGE_SIZE = 200;

/**
 * How many pages the picker will gather before it stops and says so.
 *
 * <p>
 * Four times the constitution's 250-camera production target, so the target is
 * met with room to spare. A bound exists at all because "fetch until count"
 * turns a ten-thousand-camera fab into fifty sequential requests issued while an
 * operator waits on a dialog — a worse failure than the one being fixed.
 * </p>
 *
 * <p>
 * <b>Chosen, not measured.</b> Nothing was benchmarked. What makes it safe is
 * not the number but that reaching it is reported rather than hidden: past this
 * point the picker says how many it is showing and how many exist.
 * </p>
 */
const MAXIMUM_PAGES = 5;

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
      providesTags: (_result, _error, { cameraIdentifier }) => [{ type: 'Camera' as const, id: cameraIdentifier }],
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
    renameCamera: build.mutation<void, RenameCameraInput>({
      // Same endpoint as changeCameraAddress, and exactly one of the two fields
      // per request: each is applied under its own If-Match version, so a
      // combined request's second half would quote a version its own first half
      // had just advanced (spec 033's PatchCameraRequest records this).
      query: ({ cameraIdentifier, name, version, fabId }) => ({
        url: `/${cameraIdentifier}`,
        method: 'PATCH',
        headers: ifMatch(version),
        ...(fabId !== undefined && fabId !== '' ? { params: { fabId } } : {}),
        body: { name },
      }),
      // Both: the camera itself, whose heading now shows the old name, and the
      // listing whose row does too.
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
    /**
     * Every camera an operator may put on a tile (spec 048).
     *
     * <p>
     * <b>Alongside `listCameras`, not instead of it.</b> The cameras page pages
     * this endpoint deliberately and correctly — it shows one page at a time and
     * says which. A picker needs the opposite: the whole choosable set at once,
     * because an absent option is indistinguishable from a camera that does not
     * exist.
     * </p>
     *
     * <p>
     * <b>A `queryFn` rather than `infiniteQuery`.</b> The latter models
     * user-driven "load more" and hands back an array of pages; the picker wants
     * one set, once, and its `<select>` is a React Hook Form field where every
     * change to the option-list shape is a chance to lose a selection already
     * made. Paging here keeps that shape identical.
     * </p>
     *
     * <p>
     * Ordered by <b>name</b>, not the default `registeredAt desc`. That default
     * is why the picker used to offer "the fifty most recently registered" — an
     * order no operator thinks in. Alphabetical is also what makes a native
     * select's built-in prefix type-ahead navigable, which is the mitigation
     * that lets search be a later story.
     * </p>
     */
    listAllCameraChoices: build.query<CameraChoices, { fabId?: string } | void>({
      queryFn: async (arg, _api, _extraOptions, baseQuery) => {
        const fabId = arg?.fabId;
        const gathered: CameraSummary[] = [];
        // Keyed by identifier because offset paging over a list someone else is
        // editing can deliver a camera at a page boundary twice: a registration
        // mid-loop shifts every later page down by one. Two identical options
        // and a duplicate React key is the visible symptom.
        const seen = new Set<string>();
        let count = 0;

        for (let page = 0; page < MAXIMUM_PAGES; page += 1) {
          const result = await baseQuery({
            url: '',
            method: 'GET',
            params: {
              sort: 'name',
              order: 'asc',
              offset: page * MAXIMUM_PAGE_SIZE,
              limit: MAXIMUM_PAGE_SIZE,
              ...(fabId !== undefined && fabId !== '' ? { fabId } : {}),
            },
          });

          if (result.error) {
            return { error: result.error };
          }

          const body = result.data as CameraListPage;
          count = body.count;

          for (const camera of body.items) {
            if (seen.has(camera.cameraIdentifier)) continue;
            seen.add(camera.cameraIdentifier);
            gathered.push(camera);
          }

          // A short page means the source has no more to give, so stop rather
          // than spend a request discovering an empty one. This is an
          // optimisation only — it does not decide completeness.
          if (body.items.length < MAXIMUM_PAGE_SIZE) {
            break;
          }
        }

        // One rule, and it holds for every reason the loop can end: the list is
        // complete when it holds every camera the source says exists.
        //
        // An earlier version also required that the loop had seen a short page,
        // which was wrong at exactly the bound — 1000 cameras fetched as five
        // full pages are all of them, and it reported them as incomplete. The
        // mutation that removed the extra term passed every test, which is how
        // the defect surfaced.
        return { data: { items: gathered, count, complete: gathered.length >= count } };
      },
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
  useListAllCameraChoicesQuery,
  useGetCameraQuery,
  useChangeCameraAddressMutation,
  useRenameCameraMutation,
  useRetireCameraMutation,
} = camerasApi;
