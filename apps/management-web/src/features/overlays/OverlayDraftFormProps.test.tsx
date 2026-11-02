import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayEditorProps } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';
import type { OverlayLayerListProps } from '@smart-sentinel-eye/shared/ui/composites/OverlayLayerList';
import type { OverlayEditTarget } from './OverlayDraftForm.js';

/**
 * Characterisation of what `OverlayDraftForm` hands to the two composites it
 * composes, and of how it answers their callbacks (spec 477, T003). Both
 * composites are replaced by stubs that record their latest props, so the
 * form's own guards are exercised directly: in the real tree
 * `OverlayLayerList` repeats several of them (readOnly, canAdd, canRemove),
 * which hides the form's copy. Written against the unsplit form; it must stay
 * green, unmodified, across the split.
 */

let editorProps: OverlayEditorProps | undefined;
let editorMounts = 0;
let listProps: OverlayLayerListProps | undefined;

vi.mock('@smart-sentinel-eye/shared/ui/composites/OverlayEditor', async () => {
  const { useEffect } = await import('react');
  return {
    OverlayEditor: (props: OverlayEditorProps) => {
      editorProps = props;
      useEffect(() => {
        editorMounts += 1;
      }, []);
      return <div data-testid="overlay-editor-stub" />;
    },
  };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/OverlayLayerList', () => ({
  OverlayLayerList: (props: OverlayLayerListProps) => {
    listProps = props;
    return <div data-testid="overlay-layer-list-stub" />;
  },
}));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
    useGetOverlayQuery: () => ({
      data: undefined,
      currentData: undefined,
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return { ...actual, useResolveOverlayTextQuery: () => ({ isFetching: false, isError: false }) };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }) };
});

const { OverlayDraftForm } = await import('./OverlayDraftForm.js');

beforeEach(() => {
  editorProps = undefined;
  listProps = undefined;
  editorMounts = 0;
});

afterEach(cleanup);

function text(value: string, y = 0.1): OverlayElement {
  return {
    kind: 'Text',
    color: '#9F9F9FD9',
    text: value,
    normalizedX: 0.1,
    normalizedY: y,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  };
}

function target(elements: OverlayElement[]): OverlayEditTarget {
  return {
    overlayIdentifier: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
    revisionNumber: 1,
    name: 'Line 1 readings',
    elements,
  };
}

type FormProps = Partial<Parameters<typeof OverlayDraftForm>[0]>;

function renderForm(props: FormProps = {}) {
  return render(<OverlayDraftForm onDone={() => undefined} onCancel={() => undefined} {...props} />);
}

function list(): OverlayLayerListProps {
  return listProps!;
}

function listTexts(): Array<string | undefined> {
  return list().elements.map((element) => (element.kind === 'Text' ? element.text : undefined));
}

describe('OverlayDraftForm props - forwarded to the editor', () => {
  it('Preselects the tile camera in the editor', () => {
    renderForm({ backdropCameraIdentifier: 'camera-7' });

    expect(editorProps!.initialCamera).toBe('camera-7');
  });

  it('Forwards the host canvas size to the editor', () => {
    renderForm({ canvasWidthPx: 640, canvasHeightPx: 360 });

    expect(editorProps!.canvasWidthPx).toBe(640);
    expect(editorProps!.canvasHeightPx).toBe(360);
  });

  it('Remounts the editor when the operator explicitly selects another layer', () => {
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]) });
    expect(editorMounts).toBe(1);

    act(() => list().onSelect(list().rowIds[1]!));

    expect(editorMounts).toBe(2);
    expect(editorProps!.layerName).toBe('B');
  });
});

describe('OverlayDraftForm props - the form guards its own layer callbacks', () => {
  it('Adds nothing while readOnly', () => {
    renderForm({ readOnly: true });

    act(() => list().onAdd('Text'));

    expect(list().rowIds).toHaveLength(1);
  });

  it('Adds a layer when not readOnly, up to the maximum and no further', () => {
    renderForm();

    for (let added = 0; added < 7; added++) act(() => list().onAdd('Text'));
    expect(list().rowIds).toHaveLength(8);

    act(() => list().onAdd('Text'));
    expect(list().rowIds).toHaveLength(8);
  });

  it('Removes nothing while readOnly', () => {
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]), readOnly: true });

    act(() => list().onRemove(0));

    expect(listTexts()).toEqual(['A', 'B']);
  });

  it('Moves nothing while readOnly', () => {
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]), readOnly: true });

    act(() => list().onMove(0, 1));

    expect(listTexts()).toEqual(['A', 'B']);
  });

  it('Moves a layer when not readOnly', () => {
    renderForm({ editTarget: target([text('A', 0.1), text('B', 0.5)]) });

    act(() => list().onMove(0, 1));

    expect(listTexts()).toEqual(['B', 'A']);
  });

  it('Never removes the last remaining layer', () => {
    renderForm();

    act(() => list().onRemove(0));

    expect(list().rowIds).toHaveLength(1);
  });
});
