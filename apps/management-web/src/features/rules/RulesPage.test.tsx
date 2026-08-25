import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const publishMock = vi.fn(async () => ({ data: 'ok' }));
const archiveMock = vi.fn(async () => ({ data: 'ok' }));
const listMock = vi.fn();

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
    usePublishRuleMutation: () => [publishMock, { isLoading: false }],
    useArchiveRuleMutation: () => [archiveMock, { isLoading: false }],
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

describe('RulesPage', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
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
