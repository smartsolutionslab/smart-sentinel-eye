import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useDefineVariableMutation: () => [defineMock, { isLoading: false, error: undefined, reset: vi.fn() }],
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

describe('SystemVariableDialog', () => {
  beforeEach(() => defineMock.mockClear());

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
});
