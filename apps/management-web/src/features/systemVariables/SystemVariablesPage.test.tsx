import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { Variable } from '@smart-sentinel-eye/shared/api/systemVariables.api';

// A single-fab operator is never asked which fab (ADR-0114), so the default
// keeps the existing cases reading as they did; the multi-fab case overrides it.
const assignedGroups = { current: ['/fabs/munich'] as string[] };

// Spec 036: hoisted so the archive confirmation's assertions can read it. It
// was an inline anonymous mock, which satisfied the hook and could not be
// asserted on.
const archiveMock = vi.hoisted(() => vi.fn(async () => ({ data: 'noop' })));

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

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
    useArchiveVariableMutation: () => [archiveMock, { isLoading: false }],
  };
});

const { SystemVariablesPage } = await import('./SystemVariablesPage.js');

function variable(overrides: Partial<Variable> = {}): Variable {
  return {
    variableIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
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

    // The row's own fab travels with the write: a name is unique per fab, so
    // a multi-fab operator's edit is otherwise ambiguous (spec 014).
    expect(setValueMock).toHaveBeenCalledWith({
      name: 'oeeLine1',
      value: '99.5',
      version: 0,
      fabId: 'munich',
    });
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

  // Two fabs may hold one name (spec 014). The edit buffer used to be keyed on
  // the name, so typing into one row appeared in the other and submitted
  // against the wrong fab.
  it('Keeps each fab edit buffer separate when two fabs share a variable name', async () => {
    const user = userEvent.setup();
    listMock.mockReturnValue({
      data: [
        variable({ variableIdentifier: 'aaaaaaaa-0000-0000-0000-000000000001', fab: 'munich' }),
        variable({ variableIdentifier: 'bbbbbbbb-0000-0000-0000-000000000002', fab: 'dresden' }),
      ],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    const inputs = screen.getAllByPlaceholderText(/new value/i);
    await user.type(inputs[0]!, '11');

    expect(inputs[0]).toHaveValue('11');
    expect(inputs[1]).toHaveValue('');

    await user.click(screen.getAllByRole('button', { name: /set value/i })[0]!);
    expect(setValueMock).toHaveBeenCalledWith({
      name: 'oeeLine1',
      value: '11',
      version: 0,
      fabId: 'munich',
    });
  });
});

/** Spec 036 — the confirmation. */
describe('SystemVariablesPage — archive confirmation', () => {
  function showing() {
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
  }

  async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: /^archive$/i }));
    return screen.getByRole('alertdialog');
  }

  beforeEach(() => {
    archiveMock.mockClear();
  });

  /** T012 / FR-002 — asserted as a call count, not as the dialog closing. */
  it('Archives nothing when the confirmation is dismissed', async () => {
    const user = userEvent.setup();
    showing();
    renderPage();

    const confirmation = await openConfirmation(user);
    await user.click(within(confirmation).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  it('Archives once confirmed', async () => {
    const user = userEvent.setup();
    showing();
    renderPage();

    const confirmation = await openConfirmation(user);
    expect(archiveMock).not.toHaveBeenCalled();

    await user.click(within(confirmation).getByRole('button', { name: /^archive$/i }));

    expect(archiveMock).toHaveBeenCalledTimes(1);
  });

  /**
   * T013 / FR-006. Both halves matter and neither is visible from the page:
   * the value disappearing is immediate, and the refusal is what anything
   * trying to set it will hit from then on.
   */
  it('Names the variable and says its value is cleared and can never be replaced', async () => {
    const user = userEvent.setup();
    showing();
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent('oeeLine1');
    expect(confirmation).toHaveTextContent(/cleared/i);
    expect(confirmation).toHaveTextContent(/never be given another/i);
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });
});

// #2015. Archived variables no longer come back by default, so the page has to
// ask the server for each tab rather than filter a full fetch in the browser.
// Without these the tabs could silently show nothing and every existing test
// would still pass: they mock the query hook and ignore what it is called with.
describe('SystemVariablesPage — the state filter is asked of the server', () => {
  beforeEach(() => {
    listMock.mockReset();
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    });
  });

  // Names the state rather than leaning on the server's default. Both would
  // show the same rows today, because Defined and Archived are the only two
  // states; a third would make the tab and its request disagree in silence.
  it('Asks for the Defined state, which excludes archived variables', () => {
    renderPage();

    expect(listMock).toHaveBeenCalledWith({ state: 'Defined' });
  });

  it('Asks for archived variables only when the Archived tab is chosen', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /^archived$/i }));

    expect(listMock).toHaveBeenLastCalledWith({ state: 'Archived' });
  });

  it('Widens the listing rather than filtering one when All is chosen', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /^all$/i }));

    expect(listMock).toHaveBeenLastCalledWith({ includeArchived: true });
  });
});
