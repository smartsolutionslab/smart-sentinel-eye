import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { CreateRuleArgs } from '@smart-sentinel-eye/shared/api/rules.api';

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

vi.mock('@smart-sentinel-eye/shared/api/rules.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/rules.api')>();
  return {
    ...actual,
    useCreateRuleMutation: () => [createMock, { isLoading: false, error: undefined, reset: vi.fn() }],
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

    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');

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
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');
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
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
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
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/variable name is required/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Still asks a multi-fab operator to choose a fab after an action toggle', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidRule(user);
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');
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
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');

    expect(screen.getByLabelText(/overlay/i)).toHaveValue('');
    expect(screen.getByLabelText(/duration/i)).toHaveValue(null);
  });

  it('Empties Value expression and blocks submit when only Variable name is refilled after an overlay round trip', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
    await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');
    await fill(user, screen.getByLabelText(/duration/i), '5000');

    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');
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

    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');

    expect(screen.getByLabelText(/duration/i)).toHaveValue(null);

    await user.click(screen.getByRole('button', { name: /create draft/i }));

    const durationField = screen.getByLabelText(/duration/i).closest('div');
    expect(await within(durationField!).findByRole('alert')).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Empties Variable name and Value expression once Overlay has been filled and the action toggles back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
    await fill(user, screen.getByLabelText(/value expression/i), '42');

    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await fill(user, screen.getByLabelText(/overlay/i), '123e4567-e89b-12d3-a456-426614174000');
    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');

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

    await user.selectOptions(screen.getByLabelText(/action/i), 'HighlightOverlay');
    await user.selectOptions(screen.getByLabelText(/action/i), 'SetVariableValue');

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
});

async function fillValidRule(user: ReturnType<typeof userEvent.setup>) {
  await fill(user, screen.getByLabelText(/^name$/i), 'high-oee');
  await fill(user, screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
  await fill(user, screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
  await fill(user, screen.getByLabelText(/variable name/i), 'oeeLine1');
  await fill(user, screen.getByLabelText(/value expression/i), '42');
}
