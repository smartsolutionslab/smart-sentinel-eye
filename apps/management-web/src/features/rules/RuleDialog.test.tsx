import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const createMock = vi.fn(async () => ({ data: 'ok' }));

vi.mock('@smart-sentinel-eye/shared/api/rules.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/rules.api')>();
  return {
    ...actual,
    useCreateRuleMutation: () => [createMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

const { RuleDialog } = await import('./RuleDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <RuleDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('RuleDialog', () => {
  beforeEach(() => createMock.mockClear());

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

    await user.type(screen.getByLabelText(/^name$/i), 'high-oee');
    await user.type(screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await user.type(screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await user.type(screen.getByLabelText(/variable name/i), 'oeeLine1');
    await user.type(screen.getByLabelText(/value expression/i), '42');
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

    await user.type(screen.getByLabelText(/^name$/i), 'High OEE');
    await user.type(screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await user.type(screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await user.type(screen.getByLabelText(/variable name/i), 'oeeLine1');
    await user.type(screen.getByLabelText(/value expression/i), '42');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/lowercase kebab-case/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });

  it('Requires the variable fields when the action sets a variable', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/^name$/i), 'high-oee');
    await user.type(screen.getByLabelText(/trigger kind/i), 'PlcCycleStart');
    await user.type(screen.getByLabelText(/predicate/i), '$.payload.cycleTime <= 30');
    await user.click(screen.getByRole('button', { name: /create draft/i }));

    expect(await screen.findByText(/variable name is required/i)).toBeInTheDocument();
    expect(createMock).not.toHaveBeenCalled();
  });
});
