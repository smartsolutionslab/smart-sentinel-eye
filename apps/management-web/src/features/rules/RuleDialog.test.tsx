import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { CreateRuleArgs } from '@smart-sentinel-eye/shared/api/rules.api';

/**
 * Spec 266 (issue #2335) US1 — new behaviour, RED. Replaces every existing
 * `user.selectOptions(getByLabelText(/action/i), X)` call site: once the
 * Action field is the shared `Select` primitive there is no native `<select>`
 * left for `selectOptions` to drive. This drives the Radix listbox instead
 * (click the trigger, click the option by name), so on TODAY's native select
 * every call site using it goes red against the underlying assertion it
 * feeds — expected, per spec 266 §6: "Tests edited, and why that is not a
 * moved characterisation." Every assertion downstream of this helper is
 * byte-identical to what it was before.
 */
const ACTION_LABELS: Record<'SetVariableValue' | 'HighlightOverlay' | 'SwitchWallScene', string> = {
  SetVariableValue: 'Set a system variable',
  HighlightOverlay: 'Highlight an overlay',
  SwitchWallScene: "Switch a wall's scene",
};

async function chooseAction(
  user: ReturnType<typeof userEvent.setup>,
  value: 'SetVariableValue' | 'HighlightOverlay' | 'SwitchWallScene',
) {
  await user.click(screen.getByRole('combobox', { name: /action/i }));
  await user.click(await screen.findByRole('option', { name: ACTION_LABELS[value] }));
}

/**
 * Spec 296 (#2618) US2 — the third action's own target. The wall select and
 * the target select are driven the same way `chooseAction` drives the Action
 * field: open the Radix listbox, click the option by its accessible name.
 */
async function chooseWall(user: ReturnType<typeof userEvent.setup>, wallName: string) {
  await user.click(screen.getByRole('combobox', { name: /^wall$/i }));
  await user.click(await screen.findByRole('option', { name: wallName }));
}

async function chooseTarget(user: ReturnType<typeof userEvent.setup>, targetLabel: string) {
  await user.click(screen.getByRole('combobox', { name: /target/i }));
  await user.click(await screen.findByRole('option', { name: targetLabel }));
}

/**
 * Spec 296 (#2618) US2 — module-level mocks for `walls.api`/`layouts.api`,
 * added beside the existing `rules.api` one. Both `RuleDialog` and
 * `RulesPage` render inside the real `app/store`, which mounts `wallsApi`/
 * `layoutsApi`: once the dialog's third action type reads
 * `useListWallsQuery`/`useListLayoutsQuery`, an unmocked hook issues a real
 * fetch from jsdom. Mutable so a test can vary the wall/layout lists; reset
 * to these defaults in `beforeEach` below.
 */
const DEFAULT_WALLS = [
  {
    wallIdentifier: 'wall-1',
    version: 0,
    fab: 'munich',
    name: 'Line 3 Wall',
    scenes: ['layout-a', 'layout-b'],
    showing: 'layout-a',
    sceneVersion: 0,
    showingSince: '2026-01-01T00:00:00Z',
  },
];

const DEFAULT_PUBLISHED_LAYOUTS = [
  {
    layoutIdentifier: 'layout-a',
    name: 'Line 3 rotation',
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

const wallsQueryState = { current: { data: DEFAULT_WALLS as unknown[], isLoading: false } };

// importOriginal, not a wholesale replacement (mirrors the rules.api mock
// above): `app/store.ts` wires every api slice's `reducerPath`/`reducer`
// unconditionally, so a mock that only supplied the hook would break the
// store itself, not just this dialog's data.
vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useListWallsQuery: () => wallsQueryState.current,
  };
});

const layoutsQueryState = {
  current: { data: { chains: [] as unknown[], published: DEFAULT_PUBLISHED_LAYOUTS as unknown[] }, isLoading: false },
};

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => layoutsQueryState.current,
  };
});

// Typed so `createMock.mock.calls[0][0]` narrows to the payload shape instead
// of an empty tuple — a bare `vi.fn(async () => …)` infers a zero-arg
// signature, which is what the new toggle-and-back cases index into.
const createMock = vi.fn(async (_payload: CreateRuleArgs) => ({ data: 'ok' }));

// Mutable so a test can put the operator in one fab or several; the dialog
// only asks when there is something to ask about.
const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

// Mutable so a test can put a create in flight (ADR-0151 focus-loss guard).
const mutationState = { current: { isLoading: false, error: undefined as unknown, reset: vi.fn() } };

vi.mock('@smart-sentinel-eye/shared/api/rules.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/rules.api')>();
  return {
    ...actual,
    useCreateRuleMutation: () => [createMock, mutationState.current],
  };
});

const { RuleDialog } = await import('./RuleDialog.js');

/**
 * Fills a field in one event rather than one per character.
 *
 * `user.type` sends a keystroke at a time, and each one costs a React render
 * plus a react-hook-form validation pass. Across the five fields these tests
 * fill that is fifty-six keystrokes, and it put every typing test at 1.2-1.4 s
 * on an idle machine -- close enough to the 5 s default that a loaded one
 * tipped 'Submits a SetVariableValue rule' over, intermittently, in CI.
 *
 * Nothing here tests per-character behaviour: every assertion is made after
 * submit. So the typing was work the tests never asked for.
 */
async function fill(
  user: ReturnType<typeof userEvent.setup>,
  field: ReturnType<typeof screen.getByLabelText>,
  text: string,
) {
  await user.click(field);
  await user.paste(text);
}

function renderDialog() {
  return render(
    <Provider store={store}>
      <RuleDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

// Drives the *same* mounted RuleDialog instance's `open` prop false then
// true, inside the Provider renderDialog() already rendered (mirrors
// SystemVariableDialog.test.tsx's toggleOpen). RulesPage.tsx:213 keeps the
// dialog mounted while closed, so this is what a real Cancel/Esc/overlay-click
// close does to the component instance; unmounting and remounting would reset
// every useState/useForm value for free and pass on unfixed code, proving
// nothing about issue #2579.
function toggleOpen(rerender: ReturnType<typeof render>['rerender'], open: boolean) {
  rerender(
    <Provider store={store}>
      <RuleDialog open={open} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('RuleDialog', () => {
  beforeEach(() => {
    createMock.mockClear();
    assignedGroups.current = ['/fabs/munich'];
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
    wallsQueryState.current = { data: DEFAULT_WALLS, isLoading: false };
    layoutsQueryState.current = { data: { chains: [], published: DEFAULT_PUBLISHED_LAYOUTS }, isLoading: false };
  });

  /**
   * Spec 266 (issue #2335) US1 — new behaviour, RED. The discriminating
   * assertion is the portalled `listbox`: a native `<select>` also exposes
   * role `combobox`, but never a separate `listbox` in jsdom (spec §1
   * finding 1), so this fails against today's native select and only passes
   * once the Action field is the shared `Select` primitive.
   */
  it('The action field opens a listbox of the two actions', async () => {
    const user = userEvent.setup();
    renderDialog();

    screen.getByRole('combobox', { name: /action/i }).focus();
    await user.keyboard('{ArrowDown}');

    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).getByRole('option', { name: 'Set a system variable' })).toBeInTheDocument();
    expect(within(listbox).getByRole('option', { name: 'Highlight an overlay' })).toBeInTheDocument();
  });

  it('Renders the rule fields and the AEL help panel', () => {
    renderDialog();
    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/predicate/i)).toBeInTheDocument();
    expect(screen.getByTestId('ael-help')).toBeInTheDocument();
  });

  it('Shows the SetVariableValue fields by default', () => {
    renderDialog();
    expect(screen.getByLabelText(/variable name/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/duration/i)).not.toBeInTheDocument();
  });

  it('Swaps to the HighlightOverlay fields when the action changes', async () => {
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'HighlightOverlay');

    expect(screen.getByLabelText(/duration/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/variable name/i)).not.toBeInTheDocument();
  });

  it('Submits a SetVariableValue rule', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    expect(createMock).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'high-oee',
        triggerKind: 'PlcCycleStart',
        actionType: 'SetVariableValue',
        variableName: 'oeeLine1',
      }),
    );
  });

  it('Rejects a name that is not lowercase kebab-case', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'High OEE');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/lowercase kebab-case/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Requires the variable fields when the action sets a variable', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/variable name is required/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  // ---- ADR-0114: the operator is asked only when there is a choice ----

  it('Does not ask a single-fab operator to choose, and sends no fabId', async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(screen.queryByLabelText(/^fab$/i)).not.toBeInTheDocument();

    await fillValidRule(user);
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    // No fabId at all, rather than the operator's one fab guessed at here: the
    // server infers it, and that is the behaviour ADR-0114 records.
    expect(createMock).toHaveBeenCalledWith(expect.not.objectContaining({ fabId: expect.anything() }));
  });

  it('Asks a multi-fab operator to choose, offering only their own fabs', () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    renderDialog();

    const select = screen.getByLabelText(/^fab$/i);
    expect(select).toBeInTheDocument();
    expect([...select.querySelectorAll('option')].map((option) => option.getAttribute('value'))).toEqual([
      '',
      'dresden',
      'munich',
    ]);
  });

  it('Refuses to submit a multi-fab rule with no fab chosen', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Sends the chosen fab for a multi-fab operator', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledWith(expect.objectContaining({ fabId: 'dresden' }));
  });

  /**
   * Spec 231 (#2433) US2 — new behaviour, RED. The Fab `<select>`'s `onChange`
   * sets the value but never clears `fabError`, so the "Choose which fab…"
   * message survives picking a fab and only disappears on the next submit.
   */
  it('Clears the missing-fab message as soon as a fab is chosen', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await user.click(screen.getByRole('button', { name: /create draft/i }));
    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');

    expect(screen.queryByText(/choose which fab/i)).not.toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Ignores groups that are not fab groups', () => {
    assignedGroups.current = ['/fabs/munich', '/departments/maintenance', '/fabs/dresden'];
    renderDialog();

    expect([...screen.getByLabelText(/^fab$/i).querySelectorAll('option')].map((o) => o.getAttribute('value'))).toEqual(
      ['', 'dresden', 'munich'],
    );
  });

  // ---- Spec 212: a ghost value from a branch the operator toggled away from
  // must not survive to block (or ride along with) submission ----

  it('Submits a SetVariableValue rule after the operator has looked at the overlay action and changed back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await chooseAction(user, 'HighlightOverlay');
    await chooseAction(user, 'SetVariableValue');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0];
    expect(payload).not.toHaveProperty('overlayIdentifier');
    expect(payload).not.toHaveProperty('durationMs');
    expect(payload).toEqual(expect.objectContaining({ actionType: 'SetVariableValue' }));
  });

  it('Submits a HighlightOverlay rule without the variable fields it no longer uses', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await chooseAction(user, 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');
    await fill(user, screen.getByLabelText(/duration/i), '5000');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0];
    expect(payload).not.toHaveProperty('variableName');
    expect(payload).not.toHaveProperty('valueExpression');
    expect(payload).toEqual(expect.objectContaining({ actionType: 'HighlightOverlay' }));
  });

  it('Still requires the variable fields after the action has been toggled there and back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await chooseAction(user, 'HighlightOverlay');
    await chooseAction(user, 'SetVariableValue');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/variable name is required/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Still asks a multi-fab operator to choose a fab after an action toggle', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await chooseAction(user, 'HighlightOverlay');
    await chooseAction(user, 'SetVariableValue');
    // Spec 241 (#2526): the round trip now remounts fresh, empty inputs
    // instead of carrying the values back, so they are refilled here rather
    // than relied on to have survived the toggle.
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  // ---- Spec 241 (#2526): the action ternary's two branches share the same
  // DOM position with no key, so React reuses the underlying <input> nodes
  // across a toggle instead of remounting them -- a value typed into one
  // branch's field carries into the other branch's differently-labelled
  // field, and can be submitted under the wrong name ----

  it('Empties the Overlay and Duration fields after a typed variable name is toggled away from', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await chooseAction(user, 'HighlightOverlay');

    expect(screen.getByLabelText(/overlay/i)).toHaveValue('');
    expect(screen.getByLabelText(/duration/i)).toHaveValue(null);
  });

  it('Empties Value expression and blocks submit when only Variable name is refilled after an overlay round trip', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await chooseAction(user, 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');
    await fill(user, screen.getByLabelText(/duration/i), '5000');

    await chooseAction(user, 'SetVariableValue');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');

    expect(screen.getByLabelText(/value expression/i)).toHaveValue('');

    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/value expression is required for setvariablevalue/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Empties Duration and blocks submit when only Overlay is refilled after a variable round trip', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await fill(user, screen.getByLabelText(/value expression/i), '1000');

    await chooseAction(user, 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');

    expect(screen.getByLabelText(/duration/i)).toHaveValue(null);

    await user.click(screen.getByRole('button', { name: /create draft/i }));

    const durationField = screen.getByLabelText(/duration/i).closest('div');
    const alert = await within(durationField!).findByRole('alert');
    expect(alert).toHaveTextContent(/duration is required for highlightoverlay/i);
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Reports the friendly Duration message when a HighlightOverlay rule is submitted with Duration left untouched', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');

    await chooseAction(user, 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');

    await user.click(screen.getByRole('button', { name: /create draft/i }));

    const durationField = screen.getByLabelText(/duration/i).closest('div');
    const alert = await within(durationField!).findByRole('alert');
    expect(alert).toHaveTextContent(/duration is required for highlightoverlay/i);
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Empties Variable name and Value expression once Overlay has been filled and the action toggles back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');

    await chooseAction(user, 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');
    await chooseAction(user, 'SetVariableValue');

    expect(screen.getByLabelText(/variable name/i)).toHaveValue('');
    expect(screen.getByLabelText(/value expression/i)).toHaveValue('');
  });

  it('Empties both variable fields and blocks submit on an untouched action round trip', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');

    await chooseAction(user, 'HighlightOverlay');
    await chooseAction(user, 'SetVariableValue');

    expect(screen.getByLabelText(/variable name/i)).toHaveValue('');
    expect(screen.getByLabelText(/value expression/i)).toHaveValue('');

    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/variable name is required for setvariablevalue/i)).toBeInTheDocument();
    expect(screen.getByText(/value expression is required for setvariablevalue/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  /**
   * Issue #2579 (spec 256) US3 — new behaviour, RED. Unlike the other two
   * dialogs, RuleDialog has no `if (!open) reset(...)` anywhere: its close
   * effect (`:40-46`) resets only the mutation state and fabId/fabError, and
   * the Dialog gets `onOpenChange={onOpenChange}` directly (`:109`) with no
   * wrapper to reset from either. So Cancel, Esc and overlay click all leave
   * typed values in place; `toggleOpen` models exactly what all three do to
   * this always-mounted component instance.
   */
  it('Drops the typed name, predicate and trigger source when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.x > 1');
    await user.clear(screen.getByLabelText(/trigger source/i));
    await fill(user, screen.getByLabelText(/trigger source/i), 'mqtt');

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('');
    expect(screen.getByLabelText(/predicate/i)).toHaveValue('');
    expect(screen.getByLabelText(/trigger source/i)).toHaveValue('plc');
  });

  // ---- Issue #2624 / ADR-0151: focus must survive an in-flight submit ----

  it('Announces Create draft as unavailable with aria-disabled, not native disabled, while in flight', () => {
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };

    renderDialog();

    const submit = screen.getByRole('button', { name: /^(create draft|creating…)$/i });
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(submit).not.toHaveAttribute('disabled');
  });

  it('Refuses a form-level submit while a create is in flight', async () => {
    const user = userEvent.setup();
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderDialog();

    await fillValidRule(user);
    // The submit event is dispatched at the form itself, bypassing whatever
    // the submit button's own disabled/aria-disabled state is — this is the
    // form-level guard, not a click or an implicit-submission proof (that is
    // e2e/in-flight-focus.spec.ts). One macrotask flush lets react-hook-form's
    // (async) zodResolver validation and the mocked mutation call settle
    // before asserting, since both resolve on the microtask queue with no
    // real timer involved.
    await act(async () => {
      fireEvent.submit(screen.getByTestId('rule-form'));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(createMock).not.toHaveBeenCalled();
  });

  it('Submits a form-level submit once when nothing is in flight', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await act(async () => {
      fireEvent.submit(screen.getByTestId('rule-form'));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(createMock).toHaveBeenCalledTimes(1);
  });
});

/**
 * Spec 296 (#2618) US2 — new behaviour, RED. `RuleDialog.tsx` offers only
 * `SetVariableValue`/`HighlightOverlay` today; none of these cases can pass
 * until `ACTION_OPTIONS` gains "Switch a wall's scene" and the dialog grows
 * the wall/target selects (T124). `chooseAction(user, 'SwitchWallScene')`
 * itself is the first thing to fail — there is no such option yet — so every
 * test below fails for that reason until the option exists, which is the
 * correct, uninteresting failure for a feature that has not been built.
 */
describe('RuleDialog — SwitchWallScene (spec 296 US2, new behaviour, RED)', () => {
  beforeEach(() => {
    createMock.mockClear();
    assignedGroups.current = ['/fabs/munich'];
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
    wallsQueryState.current = { data: DEFAULT_WALLS, isLoading: false };
    layoutsQueryState.current = { data: { chains: [], published: DEFAULT_PUBLISHED_LAYOUTS }, isLoading: false };
  });

  it('Renders a wall select and a target select populated with the chosen wall’s scenes by layout name', async () => {
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'SwitchWallScene');

    expect(screen.getByRole('combobox', { name: /^wall$/i })).toBeInTheDocument();
    await chooseWall(user, 'Line 3 Wall');

    await user.click(screen.getByRole('combobox', { name: /target/i }));
    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).getByRole('option', { name: 'Next scene' })).toBeInTheDocument();
    expect(within(listbox).getByRole('option', { name: 'Line 3 rotation' })).toBeInTheDocument();
    expect(within(listbox).getByRole('option', { name: 'Line 3 fault view' })).toBeInTheDocument();
  });

  it('Submits a SwitchWallScene rule targeting a specific layout', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'switch-line-3');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'LineStop');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.line == 3');
    await chooseAction(user, 'SwitchWallScene');
    await chooseWall(user, 'Line 3 Wall');
    await chooseTarget(user, 'Line 3 fault view');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0] as Record<string, unknown> | undefined;
    expect(payload).toEqual(
      expect.objectContaining({
        actionType: 'SwitchWallScene',
        wallIdentifier: 'wall-1',
        sceneTarget: 'Layout',
        targetLayoutIdentifier: 'layout-b',
      }),
    );
    expect(payload).not.toHaveProperty('variableName');
    expect(payload).not.toHaveProperty('overlayIdentifier');
  });

  it('Submits a SwitchWallScene rule targeting Next with no targetLayoutIdentifier (FR-003)', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'switch-line-3-next');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'LineStop');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.line == 3');
    await chooseAction(user, 'SwitchWallScene');
    await chooseWall(user, 'Line 3 Wall');
    await chooseTarget(user, 'Next scene');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0] as Record<string, unknown> | undefined;
    expect(payload).toEqual(expect.objectContaining({ wallIdentifier: 'wall-1', sceneTarget: 'Next' }));
    // Omitted or null — never the stale/previous layout identifier.
    expect(payload?.['targetLayoutIdentifier'] ?? null).toBeNull();
  });

  // ---- US2-16: a toggle away from SwitchWallScene must not leak its fields ----

  it('Submits no wall/target fields once the action type is switched away from SwitchWallScene', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');

    await chooseAction(user, 'SwitchWallScene');
    await chooseWall(user, 'Line 3 Wall');
    await chooseTarget(user, 'Line 3 fault view');

    await chooseAction(user, 'SetVariableValue');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0] as Record<string, unknown> | undefined;
    expect(payload).not.toHaveProperty('wallIdentifier');
    expect(payload).not.toHaveProperty('sceneTarget');
    expect(payload).not.toHaveProperty('targetLayoutIdentifier');
    expect(payload).toEqual(expect.objectContaining({ actionType: 'SetVariableValue' }));
  });

  it('Submits no stale SetVariableValue fields once switched to SwitchWallScene and back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'switch-line-3-back');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'LineStop');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.line == 3');
    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');

    await chooseAction(user, 'SwitchWallScene');
    await chooseWall(user, 'Line 3 Wall');
    await chooseTarget(user, 'Next scene');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(createMock).toHaveBeenCalledTimes(1);
    const payload = createMock.mock.calls[0]?.[0] as Record<string, unknown> | undefined;
    expect(payload).not.toHaveProperty('variableName');
    expect(payload).not.toHaveProperty('valueExpression');
    expect(payload).toEqual(expect.objectContaining({ actionType: 'SwitchWallScene', wallIdentifier: 'wall-1' }));
  });

  // ---- A4: only walls in the rule's own fab are offered ----

  it('Offers only walls in the single-fab operator’s own fab', async () => {
    wallsQueryState.current = {
      data: [
        ...DEFAULT_WALLS,
        {
          wallIdentifier: 'wall-2',
          version: 0,
          fab: 'dresden',
          name: 'Dresden Wall',
          scenes: ['layout-a'],
          showing: 'layout-a',
          sceneVersion: 0,
          showingSince: '2026-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    };
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'SwitchWallScene');
    await user.click(screen.getByRole('combobox', { name: /^wall$/i }));
    const listbox = await screen.findByRole('listbox');

    expect(within(listbox).getByRole('option', { name: 'Line 3 Wall' })).toBeInTheDocument();
    expect(within(listbox).queryByRole('option', { name: 'Dresden Wall' })).not.toBeInTheDocument();
  });

  it('Offers no walls to a multi-fab operator until a fab is chosen', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'SwitchWallScene');
    await user.click(screen.getByRole('combobox', { name: /^wall$/i }));
    const listbox = await screen.findByRole('listbox');

    expect(within(listbox).queryByRole('option', { name: 'Line 3 Wall' })).not.toBeInTheDocument();
  });

  it('Offers the chosen fab’s walls to a multi-fab operator once a fab is chosen', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    wallsQueryState.current = {
      data: [
        ...DEFAULT_WALLS,
        {
          wallIdentifier: 'wall-2',
          version: 0,
          fab: 'dresden',
          name: 'Dresden Wall',
          scenes: ['layout-a'],
          showing: 'layout-a',
          sceneVersion: 0,
          showingSince: '2026-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    };
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'SwitchWallScene');
    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');
    await user.click(screen.getByRole('combobox', { name: /^wall$/i }));
    const listbox = await screen.findByRole('listbox');

    expect(within(listbox).getByRole('option', { name: 'Dresden Wall' })).toBeInTheDocument();
    expect(within(listbox).queryByRole('option', { name: 'Line 3 Wall' })).not.toBeInTheDocument();
  });

  // ---- Changing the wall must not leave a target from a wall it no longer names ----

  it('Clears the selected target when the wall changes', async () => {
    wallsQueryState.current = {
      data: [
        ...DEFAULT_WALLS,
        {
          wallIdentifier: 'wall-2',
          version: 0,
          fab: 'munich',
          name: 'Line 4 Wall',
          scenes: ['layout-b'],
          showing: 'layout-b',
          sceneVersion: 0,
          showingSince: '2026-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    };
    const user = userEvent.setup();
    renderDialog();

    await chooseAction(user, 'SwitchWallScene');
    await chooseWall(user, 'Line 3 Wall');
    await chooseTarget(user, 'Line 3 rotation');

    await chooseWall(user, 'Line 4 Wall');

    expect(screen.getByRole('combobox', { name: /target/i })).not.toHaveTextContent('Line 3 rotation');
  });
});

async function fillValidRule(user: ReturnType<typeof userEvent.setup>) {
  await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
  await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
  await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
  await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
  await fill(user, screen.getByLabelText(/value expression/i), '42');
}
