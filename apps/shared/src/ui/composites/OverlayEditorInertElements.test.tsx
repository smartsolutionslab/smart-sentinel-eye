// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { OverlayElement, OverlayLabel } from '../../api/overlays.api.js';
import { OverlayEditor } from './OverlayEditor.js';

/**
 * Spec 441 (issue #3072) C3 / FR-005. New behaviour, RED (ADR-0139): the
 * optional `inertElements` prop paints the other elements of the set on the
 * canvas, `aria-hidden` and `pointer-events: none`, below and above the one
 * interactive label, in ordinal order. The `Absent` test is a characterisation
 * and is expected GREEN already.
 *
 * The inert nodes carry `data-testid="overlay-editor-inert-element"` and a
 * `data-kind` of Text/Box/Ellipse (test-writer's invented API).
 */

afterEach(cleanup);

const LABEL: OverlayLabel = {
  text: 'Selected',
  normalizedX: 0.25,
  normalizedY: 0.5,
  normalizedWidth: 0.25,
  normalizedHeight: 0.2,
  fontSizePx: 32,
};

function text(value: string, x = 0.1): OverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: value,
    normalizedX: x,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function box(): OverlayElement {
  return {
    kind: 'Box',
    color: '#FF0000FF',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
  };
}

function renderEditor(inertElements?: { below: OverlayElement[]; above: OverlayElement[] }) {
  return render(
    <OverlayEditor
      value={LABEL}
      onChange={() => undefined}
      canvasWidthPx={800}
      canvasHeightPx={450}
      inertElements={inertElements}
    />,
  );
}

function inertNodes(): HTMLElement[] {
  return screen.queryAllByTestId('overlay-editor-inert-element');
}

describe('OverlayEditor inert elements', () => {
  it('Paints nothing extra when the prop is absent (characterisation)', () => {
    renderEditor();
    expect(inertNodes()).toHaveLength(0);
    expect(screen.getAllByTestId('overlay-editor-label')).toHaveLength(1);
  });

  it('Paints one node per other element on the canvas', () => {
    renderEditor({ below: [text('Under')], above: [box(), text('Over')] });
    const canvas = screen.getByTestId('overlay-editor-canvas');
    const nodes = inertNodes();
    expect(nodes).toHaveLength(3);
    nodes.forEach((node) => expect(canvas).toContainElement(node));
  });

  it('Hides every inert node from assistive technology', () => {
    renderEditor({ below: [text('Under')], above: [box()] });
    expect(inertNodes()).toHaveLength(2);
    inertNodes().forEach((node) => expect(node).toHaveAttribute('aria-hidden', 'true'));
  });

  it('Lets pointer events fall through every inert node', () => {
    renderEditor({ below: [text('Under')], above: [box()] });
    expect(inertNodes()).toHaveLength(2);
    inertNodes().forEach((node) => expect(node.style.pointerEvents).toBe('none'));
  });

  it('Paints below-elements before and above-elements after the interactive label, in ordinal order', () => {
    renderEditor({ below: [text('B0', 0.1), text('B1', 0.2)], above: [text('A0', 0.3), box()] });
    const label = screen.getByTestId('overlay-editor-label');
    const nodes = inertNodes();
    expect(nodes.map((node) => node.dataset['kind'])).toEqual(['Text', 'Text', 'Text', 'Box']);
    expect(nodes[0]!.textContent).toBe('B0');
    expect(nodes[1]!.textContent).toBe('B1');
    expect(nodes[2]!.textContent).toBe('A0');
    for (const node of nodes.slice(0, 2)) {
      expect(node.compareDocumentPosition(label) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
    for (const node of nodes.slice(2)) {
      expect(node.compareDocumentPosition(label) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
    }
  });

  it('Places an inert node from its normalised geometry and the canvas size', () => {
    renderEditor({ below: [text('Under', 0.5)], above: [] });
    const node = inertNodes()[0]!;
    expect(node.style.position).toBe('absolute');
    expect(node.style.left).toBe('400px');
    expect(node.style.top).toBe('45px');
    expect(node.style.width).toBe('240px');
    expect(node.style.height).toBe('45px');
  });

  it('Shows a shape as an empty stroke with no text', () => {
    renderEditor({ below: [], above: [box()] });
    const node = inertNodes()[0]!;
    expect(node.dataset['kind']).toBe('Box');
    expect(node.textContent).toBe('');
  });

  it('Keeps the selected label the only interactive, focusable node', () => {
    renderEditor({ below: [text('Under')], above: [text('Over')] });
    expect(screen.getAllByTestId('overlay-editor-label')).toHaveLength(1);
    expect(inertNodes()).toHaveLength(2);
    inertNodes().forEach((node) => {
      expect(node.matches('button, input, [tabindex]')).toBe(false);
      expect(node.querySelector('button, input, [tabindex]')).toBeNull();
    });
  });
});
