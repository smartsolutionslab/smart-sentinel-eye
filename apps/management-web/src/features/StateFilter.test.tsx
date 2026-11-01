import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { StateFilter, labelled } from './StateFilter.js';

type RuleState = 'Draft' | 'Active';
const OPTIONS: ReadonlyArray<{ label: string; value: RuleState | undefined }> = [
  { label: 'All', value: undefined },
  { label: 'Draft', value: 'Draft' },
  { label: 'Active', value: 'Active' },
];

describe('StateFilter', () => {
  it('Renders a labelled group with one button per option', () => {
    render(<StateFilter options={OPTIONS} value={undefined} onChange={() => undefined} />);

    const group = screen.getByRole('group', { name: 'Filter by state' });
    expect(group).toBeInTheDocument();
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['All', 'Draft', 'Active']);
  });

  it('Presses only the option equal to the value', () => {
    render(<StateFilter options={OPTIONS} value="Draft" onChange={() => undefined} />);

    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Draft' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Active' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('Treats an undefined value as an option of its own', () => {
    render(<StateFilter options={OPTIONS} value={undefined} onChange={() => undefined} />);

    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Draft' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('Reports the clicked option value, including undefined', async () => {
    const onChange = vi.fn();
    render(<StateFilter options={OPTIONS} value="Active" onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Draft' }));
    await userEvent.click(screen.getByRole('button', { name: 'All' }));

    expect(onChange.mock.calls).toEqual([['Draft'], [undefined]]);
  });

  it('Uses flex gap-2 unless told otherwise, and uses a given className verbatim', () => {
    const { rerender } = render(<StateFilter options={OPTIONS} value={undefined} onChange={() => undefined} />);
    expect(screen.getByRole('group').className).toBe('flex gap-2');

    rerender(
      <StateFilter options={OPTIONS} value={undefined} onChange={() => undefined} className="mb-4 flex gap-2" />,
    );
    expect(screen.getByRole('group').className).toBe('mb-4 flex gap-2');
  });
});

describe('labelled', () => {
  it('Makes each value its own label', () => {
    expect(labelled(['All', 'Defined'] as const)).toEqual([
      { label: 'All', value: 'All' },
      { label: 'Defined', value: 'Defined' },
    ]);
  });
});
