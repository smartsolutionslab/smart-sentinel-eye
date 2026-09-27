// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Select, type SelectOption, type SelectProps } from './Select.js';
import { FormField } from '../composites/FormField.js';

/**
 * Spec 266 (issue #2335) US1 — phase 4a is BEHAVIOUR-CHANGING (ADR-0139/0144):
 * every case below must be observed RED before `Select.tsx` is a real Radix
 * wrapper.
 *
 * Committed against the signature-only stub (`Select.tsx` renders `null`, no
 * Radix import — plan.md §6), so every case fails on **content** ("unable to
 * find role combobox" / "unable to find role listbox"), never on a missing
 * module, and `pnpm typecheck` stays green throughout (ADR-0087: this commit
 * builds on its own). The US5 dependency guard
 * (`SharedUiDependencyUsageTests`) stays red until this file's implementation
 * lands and imports `@radix-ui/react-select`.
 */
afterEach(cleanup);

const OPTIONS: readonly SelectOption[] = [
  { value: 'SetVariableValue', label: 'Set a system variable' },
  { value: 'HighlightOverlay', label: 'Highlight an overlay' },
];

function renderSelect(overrides: Partial<SelectProps> = {}) {
  const onValueChange = vi.fn();
  render(
    <Select
      id="rule-action-type"
      value="SetVariableValue"
      onValueChange={onValueChange}
      options={OPTIONS}
      {...overrides}
    />,
  );
  return { onValueChange };
}

describe('Select', () => {
  it('ArrowDown on the trigger opens a listbox with the option names, and the selected option is marked selected', async () => {
    const user = userEvent.setup();
    renderSelect();

    screen.getByRole('combobox').focus();
    await user.keyboard('{ArrowDown}');

    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).getByRole('option', { name: 'Set a system variable' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(within(listbox).getByRole('option', { name: 'Highlight an overlay' })).toBeInTheDocument();
  });

  it('Choosing an option calls onValueChange with its value', async () => {
    const user = userEvent.setup();
    const { onValueChange } = renderSelect();

    screen.getByRole('combobox').focus();
    await user.keyboard('{ArrowDown}');
    await user.click(await screen.findByRole('option', { name: 'Highlight an overlay' }));

    expect(onValueChange).toHaveBeenCalledWith('HighlightOverlay');
  });

  it('Escape closes the listbox, returns focus to the trigger, and leaves the value unchanged', async () => {
    const user = userEvent.setup();
    const { onValueChange } = renderSelect();

    const trigger = screen.getByRole('combobox');
    trigger.focus();
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('listbox');

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(onValueChange).not.toHaveBeenCalled();
  });

  it('Is reachable by its label when wrapped in a FormField', () => {
    render(
      <FormField label="Action" htmlFor="rule-action-type">
        <Select id="rule-action-type" value="SetVariableValue" onValueChange={vi.fn()} options={OPTIONS} />
      </FormField>,
    );

    expect(screen.getByLabelText('Action')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Action' })).toBeInTheDocument();
  });

  it('A disabled trigger cannot be opened by pointer', async () => {
    const user = userEvent.setup();
    renderSelect({ disabled: true });

    await user.click(screen.getByRole('combobox'));

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('Throws when an option has an empty value, naming the option label', () => {
    const badOptions: readonly SelectOption[] = [
      { value: '', label: 'Nothing chosen' },
      { value: 'x', label: 'Something' },
    ];

    expect(() => render(<Select id="bad-select" value="x" onValueChange={vi.fn()} options={badOptions} />)).toThrow(
      /Nothing chosen/,
    );
  });

  it('Shows the placeholder when value is undefined, rather than treating "" as an option', () => {
    renderSelect({ value: undefined, placeholder: 'Choose an action…' });

    expect(screen.getByText('Choose an action…')).toBeInTheDocument();
  });

  it('Forwards aria-invalid and aria-describedby to the trigger', () => {
    renderSelect({ 'aria-invalid': true, 'aria-describedby': 'rule-action-type-error' });

    const trigger = screen.getByRole('combobox');
    expect(trigger).toHaveAttribute('aria-invalid', 'true');
    expect(trigger).toHaveAttribute('aria-describedby', 'rule-action-type-error');
  });
});
