import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { z } from 'zod';
import { store } from '../../app/store.js';
import type { DefineVariableInput } from '@smart-sentinel-eye/shared/api/systemVariables.api';

// Spec 212 (issue #2430) S12: the summary must guard the *class* of defect —
// a hidden-branch error with nowhere to render — not merely the one instance
// T002's unregister effect already closes. There is no longer a real-UI path
// that leaves `truthyLabel` set while Type is String, so this one extra
// superRefine issue (fired only for this sentinel name) is how the case is
// reached at all; every other test still validates through the real schema.
const GHOST_TRUTHY_LABEL_NAME = 'ghostTruthyLabel';

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.schema', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.schema')>();
  return {
    ...actual,
    defineVariableSchema: actual.defineVariableSchema.superRefine((value, ctx) => {
      if (value.name === GHOST_TRUTHY_LABEL_NAME) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['truthyLabel'],
          message: 'BooleanLabels can only be set on Boolean variables.',
        });
      }
    }),
  };
});

// A single-fab operator is never asked which fab (ADR-0114), so the default
// keeps the existing cases reading as they did; the multi-fab case overrides it.
const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

// Typed so `defineMock.mock.calls[0][0]` narrows to the payload shape instead
// of an empty tuple — a bare `vi.fn(async () => …)` infers a zero-arg
// signature, which is what the new toggle-and-back cases index into.
const defineMock = vi.fn(async (_payload: DefineVariableInput & { fabId?: string }) => ({ data: 'noop' }));

// Mutable so a test can put a define in flight (ADR-0151 focus-loss guard).
const mutationState = { current: { isLoading: false, error: undefined as unknown, reset: vi.fn() } };

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useDefineVariableMutation: () => [defineMock, mutationState.current],
  };
});

const { SystemVariableDialog } = await import('./SystemVariableDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <SystemVariableDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

// Module-level copy for this single-fab describe block: drives the *same*
// mounted SystemVariableDialog instance's `open` prop false then true, inside
// the Provider renderDialog() already rendered. This mirrors what a real
// Cancel/Esc/overlay-click close does to the component instance
// (SystemVariablesPage.tsx:220 keeps the dialog mounted while closed);
// unmounting and remounting would reset every useState/useForm value for
// free and pass on unfixed code, proving nothing about issue #2579. The
// nested `toggleOpen` inside the multi-fab describe below is untouched.
function toggleOpen(rerender: ReturnType<typeof render>['rerender'], open: boolean) {
  rerender(
    <Provider store={store}>
      <SystemVariableDialog open={open} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('SystemVariableDialog', () => {
  beforeEach(() => {
    defineMock.mockClear();
    // Spec 231 (#2433): the multi-fab describe block below overrides this;
    // reset it here so that override cannot leak into a test that runs after
    // it — every other test in this file exercises the single-fab, no-select
    // path (ADR-0114).
    assignedGroups.current = ['/fabs/munich'];
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
  });

  it('Renders the name input and the type selector', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /type/i })).toBeInTheDocument();
  });

  it('Submits a String variable with no initial value', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledWith({
      name: 'lineStatus',
      type: 'String',
      initialValue: '',
    });
  });

  it('Surfaces a validation error when the name does not match the grammar', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), '1bad');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByText(/must start with a letter/i)).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Reveals the truthy/falsy label inputs when Type is Boolean', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    expect(screen.getByLabelText(/truthy label/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/falsy label/i)).toBeInTheDocument();
  });

  it('Submits a String variable after the operator has looked at Boolean and changed back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledTimes(1);
    expect(defineMock.mock.calls[0]?.[0]).not.toHaveProperty('truthyLabel');
    expect(defineMock.mock.calls[0]?.[0]).not.toHaveProperty('falsyLabel');
    expect(defineMock).toHaveBeenCalledWith(expect.objectContaining({ name: 'lineStatus', type: 'String' }));
  });

  // The unregister effect drops truthyLabel/falsyLabel from form state on
  // leaving Boolean; re-selecting Boolean re-registers them from the input's
  // own defaultValue="Yes"/"No" (SystemVariableDialog.tsx), not from
  // whatever the operator typed before switching away. An operator who
  // customises the labels, looks at String, and comes back loses their
  // wording — an accepted consequence of the fix (spec 212), not a
  // guarantee this test makes. This case only pins the untouched round trip.
  it('Re-registers the Boolean labels at their defaults when the operator toggles there, away, and back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'doorOpen');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledTimes(1);
    expect(defineMock).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'doorOpen', type: 'Boolean', truthyLabel: 'Yes', falsyLabel: 'No' }),
    );
  });

  it('Still refuses a name that breaks the grammar after a type toggle', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), '1bad');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByText(/must start with a letter/i)).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Still refuses a Boolean variable whose truthy label has been cleared', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'doorOpen');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.clear(screen.getByLabelText(/truthy label/i));
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Surfaces a hidden-branch error in the always-mounted summary when Type is String', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), GHOST_TRUTHY_LABEL_NAME);
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('BooleanLabels can only be set on Boolean variables.');
    expect(defineMock).not.toHaveBeenCalled();
  });

  /**
   * Issue #2579 (spec 256) US1 — new behaviour, RED. Cancel calls the
   * parent's onOpenChange(false) directly, bypassing the Dialog's own
   * onOpenChange wrapper (`:111-114`), which is the only place today's code
   * resets the form. The close effect (`:40-52`) resets the mutation state
   * and fabId/fabError but not the form, so a typed Name survives a
   * close/reopen that never touches the wrapper.
   */
  it('Drops the typed name when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.getByLabelText(/name/i)).toHaveValue('');
  });

  /**
   * Issue #2579 (spec 256) US1 — new behaviour, RED. Same gap as above, for
   * the Type selector: it should be back at its default "String" after a
   * close/reopen that never routes through Cancel or the Dialog's own
   * onOpenChange.
   */
  it('Drops the changed type when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Number');

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.getByRole('combobox', { name: /type/i })).toHaveValue('String');
  });

  // ---- Issue #2624 / ADR-0151: focus must survive an in-flight submit ----

  it('Announces Define as unavailable with aria-disabled, not native disabled, while in flight', () => {
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };

    renderDialog();

    const submit = screen.getByRole('button', { name: /^(define|saving…)$/i });
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(submit).not.toHaveAttribute('disabled');
  });

  it('Refuses a form-level submit while a define is in flight', async () => {
    const user = userEvent.setup();
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    // Dialog renders through a Radix Portal into document.body, outside
    // render()'s own container, so the form is looked up from the document.
    // The submit event is dispatched at the form itself, bypassing whatever
    // the submit button's own disabled/aria-disabled state is — this is the
    // form-level guard, not a click or an implicit-submission proof (that is
    // e2e/in-flight-focus.spec.ts). One macrotask flush lets react-hook-form's
    // (async) zodResolver validation and the mocked mutation call settle
    // before asserting, since both resolve on the microtask queue with no
    // real timer involved.
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Submits a form-level submit once when nothing is in flight', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(defineMock).toHaveBeenCalledTimes(1);

    // This describe's beforeEach clears defineMock before each of its own
    // tests, but the sibling multi-fab describe below has no beforeEach of
    // its own for it — a call left on the record by whichever test runs last
    // here would otherwise leak into that describe's first test.
    defineMock.mockClear();
  });
});

/**
 * Spec 231 (#2433) US2 — new behaviour, RED, and the first multi-fab test in
 * this file. The comment on `assignedGroups` above claims "the multi-fab case
 * overrides it", but until now nothing did: `RegisterCameraDialog.test.tsx`
 * and `RuleDialog.test.tsx` both have an ADR-0114 multi-fab section; this file
 * had none. `beforeEach` above restores single-fab so this section's override
 * cannot leak into any test that runs after it.
 */
describe('SystemVariableDialog — multi-fab (ADR-0114, spec 231 US2)', () => {
  beforeEach(() => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
  });

  async function fillValidVariable(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
  }

  // Drives the *same* mounted `SystemVariableDialog` instance's `open` prop
  // false then true, inside the Provider `renderDialog()` already rendered
  // (mirrors `LayoutEditorDialog.test.tsx`'s close/reopen `rerender` pattern).
  // `SystemVariablesPage.tsx:220` keeps the dialog mounted while closed, so
  // this is what a real Cancel does to `fabId`/`fabError`'s component
  // instance — unmounting and remounting a fresh one would reset every
  // `useState` for free and pass on the unfixed code, proving nothing about
  // issue #2561.
  function toggleOpen(rerender: ReturnType<typeof render>['rerender'], open: boolean) {
    rerender(
      <Provider store={store}>
        <SystemVariableDialog open={open} onOpenChange={() => {}} />
      </Provider>,
    );
  }

  it('Asks a multi-fab operator which fab', () => {
    renderDialog();

    expect(screen.getByLabelText(/^fab$/i)).toBeInTheDocument();
  });

  it('Refuses to submit for a multi-fab operator with no fab chosen', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidVariable(user);
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  /**
   * The Fab `<select>`'s `onChange` sets the value but never clears
   * `fabError`, so the "Choose which fab…" message survives picking a fab and
   * only disappears on the next submit.
   */
  it('Clears the missing-fab message as soon as a fab is chosen', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidVariable(user);
    await user.click(screen.getByRole('button', { name: /define/i }));
    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');

    expect(screen.queryByText(/choose which fab/i)).not.toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  /**
   * Issue #2561 (spec 242) — new behaviour, RED. `SystemVariableDialog.tsx`'s
   * close effect (`:39-41`) only calls `resetMutationState()`; unlike
   * `RegisterCameraDialog.tsx:33-46`'s matching effect, it never resets
   * `fabId`/`fabError`. Cancel calls the parent's `onOpenChange(false)`
   * directly, bypassing the Dialog's own `onOpenChange`, so only the effect
   * watching `open` can catch it.
   */
  it('Drops the missing-fab message when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await fillValidVariable(user);
    await user.click(screen.getByRole('button', { name: /define/i }));
    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.queryByText(/choose which fab/i)).not.toBeInTheDocument();
  });

  it('Drops the previously chosen fab when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await fillValidVariable(user);
    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');
    expect(screen.getByLabelText(/^fab$/i)).toHaveValue('dresden');

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.getByLabelText(/^fab$/i)).toHaveValue('');
  });
});
