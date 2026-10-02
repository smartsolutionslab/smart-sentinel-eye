import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const publishMock = vi.fn(async () => ({ data: 'ok' }));
const archiveMock = vi.fn(async () => ({ data: 'ok' }));
const listMock = vi.fn();

// Spec 296 (#2618) US2 — RulesPage's `describeAction` needs wall/layout
// *names* for a SwitchWallScene row (plan.md §4.2), which the DTO does not
// carry. Mutable so individual tests can vary the lists; module-level,
// mirroring the `rules.api` mock below.
const wallsListMock = vi.fn();
const layoutsListMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useListWallsQuery: (...args: unknown[]) => wallsListMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => layoutsListMock(...args),
  };
});

// The mutation *state* the page reads back, not just the trigger. Held in
// mutable module state because a vi.mock factory is hoisted above every test:
// the tests that care set these in-place, and beforeEach clears them.
let publishState: { isLoading: boolean; error?: unknown } = { isLoading: false };
let archiveState: { isLoading: boolean; error?: unknown } = { isLoading: false };

/** An RTK Query error in the shape the gateway's RFC-7807 body arrives in. */
function refusal(status: number, title: string, detail?: string) {
  return { status, data: { title, status, ...(detail === undefined ? {} : { detail }) } };
}

function rule(overrides: Record<string, unknown> = {}) {
  return {
    ruleIdentifier: '019f-aaaa',
    version: 0,
    fab: 'munich',
    name: 'high-oee',
    triggerSource: 'plc',
    triggerKind: 'PlcCycleStart',
    predicate: '$.payload.cycleTime <= 30',
    action: {
      kind: 'SetVariableValue',
      variableName: 'oeeLine1',
      valueExpression: '100 - $.payload.cycleTime * 2',
      overlay: null,
      durationMs: null,
    },
    state: 'Draft',
    createdAt: '2026-05-28T08:00:00Z',
    createdBy: '019f-bbbb',
    publishedAt: null,
    archivedAt: null,
    ...overrides,
  };
}

// The page renders RuleDialog, which reads the operator's fabs from the OIDC
// claims. One fab: the fab is inferred and no selector appears (ADR-0114).
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: ['/fabs/munich'] } } }),
}));

vi.mock('@smart-sentinel-eye/shared/api/rules.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/rules.api')>();
  return {
    ...actual,
    useListRulesQuery: (...args: unknown[]) => listMock(...args),
    usePublishRuleMutation: () => [publishMock, publishState],
    useArchiveRuleMutation: () => [archiveMock, archiveState],
    useCreateRuleMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
    useDryRunRuleMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

const { RulesPage } = await import('./RulesPage.js');

function renderPage() {
  return render(
    <Provider store={store}>
      <RulesPage />
    </Provider>,
  );
}

/** Default wall/layout fixtures for the SwitchWallScene describe tests below. */
const DEFAULT_WALLS = [
  {
    wallIdentifier: 'wall-1',
    version: 0,
    fab: 'munich',
    name: 'Line 3 rotation',
    scenes: ['layout-a', 'layout-b'],
    showing: 'layout-a',
    sceneVersion: 0,
    showingSince: '2026-01-01T00:00:00Z',
  },
];

const DEFAULT_PUBLISHED_LAYOUTS = [
  {
    layoutIdentifier: 'layout-a',
    name: 'Scene A',
    revisionNumber: 1,
    gridRows: 1,
    gridCols: 1,
    tiles: [],
    publishedAt: '2026-01-01T00:00:00Z',
  },
  {
    layoutIdentifier: 'layout-b',
    name: 'Line 3 fault view',
    revisionNumber: 1,
    gridRows: 1,
    gridCols: 1,
    tiles: [],
    publishedAt: '2026-01-01T00:00:00Z',
  },
];

function resetWallsAndLayoutsMocks() {
  wallsListMock.mockReset();
  wallsListMock.mockReturnValue({ data: DEFAULT_WALLS, isLoading: false, isError: false, refetch: vi.fn() });
  layoutsListMock.mockReset();
  layoutsListMock.mockReturnValue({
    data: { chains: [], published: DEFAULT_PUBLISHED_LAYOUTS },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  });
}

describe('RulesPage', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
    publishState = { isLoading: false };
    archiveState = { isLoading: false };
    resetWallsAndLayoutsMocks();
    listMock.mockReset();
    listMock.mockReturnValue({ data: [rule()], isLoading: false, isError: false, refetch: vi.fn() });
  });

  it('Shows an empty state when there are no rules', () => {
    listMock.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getByText(/no rules yet/i)).toBeInTheDocument();
  });

  it('Renders a rule with its trigger, predicate and action', () => {
    renderPage();
    expect(screen.getByText('high-oee')).toBeInTheDocument();
    expect(screen.getByText('plc/PlcCycleStart')).toBeInTheDocument();
    expect(screen.getByText('$.payload.cycleTime <= 30')).toBeInTheDocument();
    expect(screen.getByText(/Set oeeLine1 =/)).toBeInTheDocument();
  });

  /**
   * Spec 297 (issue #2635) T004. Characterisation, observed green on the
   * untouched tree (`ab030e5a`) before any source edit. Pins what the State
   * cell's Badge conversion must keep: the text is exactly the rule's state
   * name, for each state — not the tone, not the markup. Must pass unmodified
   * after the conversion (ADR-0139/0144 §6).
   */
  it.each(['Draft', 'Active', 'Archived'] as const)('Shows the State cell text as exactly %s', (state) => {
    listMock.mockReturnValue({ data: [rule({ state })], isLoading: false, isError: false, refetch: vi.fn() });
    renderPage();
    expect(within(screen.getByRole('table')).getByText(state)).toBeInTheDocument();
  });

  /**
   * Spec 297 (issue #2635) T011, US3, FR-007. RED: today's `StateBadge`
   * (`RulesPage.tsx:224-228`) is coloured text only (`text-xs font-medium
   * ${tone}`, no fill, no padding) — not a pill. After the conversion it
   * renders through the shared Badge, carrying the matching tone class.
   */
  it.each([
    ['Active', 'bg-accent-active-subtle'],
    ['Draft', 'bg-accent-warning-subtle'],
    ['Archived', 'bg-bg-raised'],
  ] as const)('Renders the %s State cell as a Badge with the %s tone', (state, toneClass) => {
    listMock.mockReturnValue({ data: [rule({ state })], isLoading: false, isError: false, refetch: vi.fn() });
    renderPage();

    const cell = within(screen.getByRole('table')).getByText(state);
    expect(cell.className).toContain(toneClass);
  });

  it('Describes a HighlightOverlay action by its duration', () => {
    listMock.mockReturnValue({
      data: [
        rule({
          action: {
            kind: 'HighlightOverlay',
            variableName: null,
            valueExpression: null,
            overlay: '019f-cccc',
            durationMs: 5000,
          },
        }),
      ],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderPage();
    expect(screen.getByText(/Highlight overlay for 5000 ms/)).toBeInTheDocument();
  });

  // Both carry the row's own fab. A name is unique per fab rather than
  // globally, so without it a multi-fab operator is refused outright — and the
  // rule is already on screen, so there is nothing to ask.
  it('Publishes a Draft rule from its row action, naming the rule’s fab', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: 'Publish' }));
    expect(publishMock).toHaveBeenCalledWith({ name: 'high-oee', version: 0, fabId: 'munich' });
  });

  /**
   * Spec 036 T017 — the one existing test this feature could not leave alone.
   *
   * <p>
   * It used to click Archive and assert the request. Archiving now asks first,
   * so the click alone sends nothing. The confirmation step is <b>added</b> and
   * the original assertion is <b>kept</b>: deleting it would have been the
   * quickest way to green and would have removed the only check that archiving
   * still sends the right request — at exactly the moment the path to it
   * changed.
   * </p>
   *
   * <p>
   * Kept, it now proves two things instead of one: that the confirmation is
   * required, and that confirming sends precisely what it sent before.
   * </p>
   */
  it('Archives a rule once confirmed, naming the rule’s fab', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Archive' }));

    // The click asks; it does not archive.
    expect(archiveMock).not.toHaveBeenCalled();

    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Archive' }));

    expect(archiveMock).toHaveBeenCalledWith({ name: 'high-oee', version: 0, fabId: 'munich' });
  });

  /**
   * FR-002, asserted as a **call count**. A confirmation that closes cleanly
   * and archives anyway passes any assertion about the dialog closing.
   */
  it('Archives nothing when the confirmation is dismissed', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Archive' }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  /**
   * FR-003. Names the rule, and says what an archived rule costs — that a
   * replacement means cloning, which is the part an operator cannot infer.
   */
  it('Names the rule and says a replacement means cloning it', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Archive' }));

    const confirmation = screen.getByRole('alertdialog');
    expect(confirmation).toHaveTextContent('high-oee');
    expect(confirmation).toHaveTextContent(/cannot be published again/i);
    expect(confirmation).toHaveTextContent(/cloning/i);
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });

  it('Shows each rule’s fab, so two rows sharing a name can be told apart', () => {
    renderPage();
    expect(screen.getByTestId('rule-fab')).toHaveTextContent('munich');
  });

  it('Offers no Publish action for an Active rule', () => {
    listMock.mockReturnValue({
      data: [rule({ state: 'Active' })],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderPage();
    expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument();
  });

  it('Offers neither Publish nor Archive for an Archived rule', () => {
    listMock.mockReturnValue({
      data: [rule({ state: 'Archived' })],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderPage();
    expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument();
  });

  it('Passes the selected state filter to the query', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: 'Active' }));
    expect(listMock).toHaveBeenLastCalledWith({ state: 'Active' });
  });

  it('Queries without filters when All is selected', () => {
    renderPage();
    expect(listMock).toHaveBeenLastCalledWith(undefined);
  });

  it('Toggles the dry-run panel from a row action', async () => {
    const user = userEvent.setup();
    renderPage();
    expect(screen.queryByTestId('dry-run-panel')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Dry run' }));
    expect(screen.getByTestId('dry-run-panel')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Hide dry run' }));
    expect(screen.queryByTestId('dry-run-panel')).not.toBeInTheDocument();
  });

  it('Offers a retry when the list fails to load', async () => {
    const refetch = vi.fn();
    listMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    const user = userEvent.setup();
    renderPage();

    expect(screen.getByRole('alert')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalled();
  });
});

/**
 * #1952. Both mutations used to discard their result, so a refused publish or
 * archive told the operator nothing — and on publish it was worse than nothing:
 * the list refetched, the row flipped to Published (by the *other* operator),
 * and the refusal read as success.
 *
 * These assert on the rendered alert rather than on the mutation state, because
 * capturing the error and not rendering it is exactly as silent as before.
 */
describe('RulesPage — a refused mutation is surfaced', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
    publishState = { isLoading: false };
    archiveState = { isLoading: false };
    resetWallsAndLayoutsMocks();
    listMock.mockReset();
    listMock.mockReturnValue({ data: [rule()], isLoading: false, isError: false, refetch: vi.fn() });
  });

  it('Shows the server’s reason when a publish is refused as stale', () => {
    publishState = {
      isLoading: false,
      error: refusal(
        409,
        'RULE_STALE',
        "Rule 'high-oee' has changed since version 0 (now 1). Re-read it and reapply the change.",
      ),
    };
    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent(/has changed since version 0/i);
  });

  it('Shows the server’s reason when an archive is refused', () => {
    archiveState = {
      isLoading: false,
      error: refusal(409, 'RULE_STALE', 'Rule has changed. Re-read it and reapply the change.'),
    };
    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent(/re-read it/i);
  });

  /**
   * A 409 without a detail is the case the wording matters most for: with
   * nothing from the server the page has to say it itself, and "try again" is
   * the one thing it must not say — resubmitting unchanged replays the stale
   * intent over whoever wrote in between (ADR-0113).
   */
  it('Tells a stale operator to reload rather than to try again', async () => {
    const refetch = vi.fn();
    listMock.mockReturnValue({ data: [rule()], isLoading: false, isError: false, refetch });
    publishState = { isLoading: false, error: refusal(409, 'RULE_STALE') };

    const user = userEvent.setup();
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/reload/i);
    expect(alert).not.toHaveTextContent(/try again/i);

    await user.click(screen.getByRole('button', { name: /reload/i }));
    expect(refetch).toHaveBeenCalled();
  });

  /**
   * A 400 is the operator's own mistake, not someone else's write. Offering
   * Reload there is advice that does nothing, and the lost-update wording would
   * blame a writer who does not exist.
   */
  it('Offers no reload for a refusal that is not a conflict', () => {
    publishState = { isLoading: false, error: refusal(400, 'RULE_INVALID', 'The predicate does not parse.') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/does not parse/i);
    expect(alert).not.toHaveTextContent(/someone else/i);
    expect(screen.queryByRole('button', { name: /reload/i })).not.toBeInTheDocument();
  });

  it('Shows no alert while both mutations are clean', () => {
    renderPage();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /**
   * Spec 298 (issue #2693) US1 — new behaviour, RED (ADR-0139/0144). The
   * refusal box still paints the call-site alpha blend
   * (`bg-accent-fault/10`), not the opaque `FaultNotice` tint.
   */
  it('Shows a refused change on the shared fault notice, not call-site alpha', () => {
    publishState = { isLoading: false, error: refusal(409, 'RULE_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
    expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
  });
});

/**
 * Spec 298 (issue #2693) US2 — new behaviour, RED (ADR-0139/0144). The load
 * failure here (`RulesPage.tsx:170`) is the one genuine retry among the six
 * sites this spec touches, and the only one painting a *different* box
 * today — border only, no fill, default text colour, a `secondary` Button
 * instead of a link-style Retry. It becomes `RetryBanner`.
 */
describe('RulesPage — a failed load is the shared RetryBanner (spec 298 US2)', () => {
  beforeEach(() => {
    listMock.mockReset();
    publishState = { isLoading: false };
    archiveState = { isLoading: false };
    resetWallsAndLayoutsMocks();
  });

  it('Shows the failed load on the fault notice tokens, with a link-style Retry', () => {
    const refetch = vi.fn();
    listMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not load rules.');
    expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
    expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
    expect(within(alert).getByRole('button', { name: 'Retry' })).toHaveClass('underline');
  });
});

/**
 * Spec 296 (#2618) US2 — new behaviour, RED (ADR-0139/0144). `describeAction`
 * (`RulesPage.tsx:213-217`) is a binary ternary today: anything that is not
 * `SetVariableValue` renders as a `HighlightOverlay` description, so a
 * `SwitchWallScene` row renders "Highlight overlay for null ms" until T124's
 * exhaustive rewrite. The wording below ("Switch <wall> to <target>") is the
 * contract this PR sets for that rewrite, following plan.md §4.2's own
 * example wording.
 */
describe('RulesPage — SwitchWallScene action descriptions (spec 296 US2, new behaviour, RED)', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
    publishState = { isLoading: false };
    archiveState = { isLoading: false };
    resetWallsAndLayoutsMocks();
    listMock.mockReset();
  });

  function switchWallSceneRule(overrides: Record<string, unknown> = {}) {
    return rule({
      action: {
        kind: 'SwitchWallScene',
        variableName: null,
        valueExpression: null,
        overlay: null,
        durationMs: null,
        wall: 'wall-1',
        sceneTarget: 'Layout',
        targetLayout: 'layout-b',
        ...overrides,
      },
    });
  }

  it('Describes a SwitchWallScene action targeting a specific layout, by wall and layout name', () => {
    listMock.mockReturnValue({ data: [switchWallSceneRule()], isLoading: false, isError: false, refetch: vi.fn() });
    renderPage();

    expect(screen.getByText(/Switch Line 3 rotation to Line 3 fault view/i)).toBeInTheDocument();
  });

  it('Describes a SwitchWallScene action targeting Next, by wall name', () => {
    listMock.mockReturnValue({
      data: [switchWallSceneRule({ sceneTarget: 'Next', targetLayout: null })],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderPage();

    expect(screen.getByText(/Switch Line 3 rotation to its next scene/i)).toBeInTheDocument();
  });

  it('Falls back to the raw wall identifier when the wall lookup does not have it', () => {
    wallsListMock.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    listMock.mockReturnValue({
      data: [switchWallSceneRule({ wall: 'wall-missing' })],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderPage();

    expect(screen.getByText(/wall-missing/)).toBeInTheDocument();
  });
});
