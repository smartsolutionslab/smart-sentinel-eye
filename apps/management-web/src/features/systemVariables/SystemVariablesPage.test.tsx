import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { Variable } from '@smart-sentinel-eye/shared/api/systemVariables.api';

const listMock = vi.fn();
const setValueMock = vi.fn(async () => ({ data: 'noop' }) as unknown);
let setValueState: { isLoading: boolean; error?: unknown } = { isLoading: false };
const defineMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useListVariablesQuery: (...args: unknown[]) => listMock(...args),
    useSetVariableValueMutation: () => [setValueMock, setValueState],
    useDefineVariableMutation: () => [defineMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useArchiveVariableMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false }],
  };
});

const { SystemVariablesPage } = await import('./SystemVariablesPage.js');

function variable(overrides: Partial<Variable> = {}): Variable {
  return {
    variableIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    name: 'oeeLine1',
    type: 'Number',
    state: 'Defined',
    value: null,
    truthyLabel: null,
    falsyLabel: null,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    ...overrides,
  };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <SystemVariablesPage />
    </Provider>,
  );
}

describe('SystemVariablesPage', () => {
  beforeEach(() => {
    setValueState = { isLoading: false };
    setValueMock.mockImplementation(async () => ({ data: 'noop' }) as unknown);
    listMock.mockReset();
    setValueMock.mockClear();
  });

  it('Shows an empty-state message when there are no variables', () => {
    listMock.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/no system variables to show/i)).toBeInTheDocument();
  });

  it('Renders one row per variable with type, state and current value', () => {
    listMock.mockReturnValue({
      data: [variable({ value: '82.4' })],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByRole('heading', { name: 'oeeLine1' })).toBeInTheDocument();
    expect(screen.getByText(/82\.4/)).toBeInTheDocument();
  });

  it('Renders the unset placeholder when value is null', () => {
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/\(unset\)/)).toBeInTheDocument();
  });

  it('Submitting a new value calls setVariableValue with the typed string', async () => {
    const user = userEvent.setup();
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    const input = screen.getByPlaceholderText(/new value/i);
    await user.type(input, '99.5');
    await user.click(screen.getByRole('button', { name: /set value/i }));

    expect(setValueMock).toHaveBeenCalledWith({ name: 'oeeLine1', value: '99.5', version: 0 });
  });

  // The page used to clear the pending edit unconditionally, so a rejected
  // write looked exactly like a successful one: the typed value vanished and
  // the old one came back with no explanation. On a conflict that loses the
  // operator's work twice — once to the other writer, once to the UI.
  it('Keeps the typed value when the write is rejected', async () => {
    const user = userEvent.setup();
    setValueMock.mockImplementation(async () => ({ error: { status: 409, data: {} } }) as unknown);
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    const input = screen.getByPlaceholderText(/new value/i);
    await user.type(input, '99.5');
    await user.click(screen.getByRole('button', { name: /set value/i }));

    expect(input).toHaveValue('99.5');
  });

  it('Surfaces a rejected write instead of swallowing it', async () => {
    setValueState = {
      isLoading: false,
      error: { status: 409, data: { detail: 'Variable moved on. Re-read it and reapply the change.' } },
    };
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent(/re-read it and reapply/i);
  });

  // Never "Try again": retrying replays the same stale intent over whoever
  // wrote in between, which is the overwrite this work removes.
  it('Offers Reload rather than a retry on a conflict', async () => {
    setValueState = { isLoading: false, error: { status: 409, data: { detail: 'Conflict.' } } };
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /try again/i })).not.toBeInTheDocument();
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });
});
