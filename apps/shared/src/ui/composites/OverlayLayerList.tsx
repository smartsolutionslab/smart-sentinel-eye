import { useEffect, useRef, useState } from 'react';
import type { OverlayElement } from '../../api/overlays.api.js';
import { MAX_ELEMENTS } from '../../api/overlays.schema.js';
import { Button } from '../primitives/Button.js';
import { DropdownMenu } from '../primitives/DropdownMenu.js';
import { ToggleChip } from '../primitives/ToggleChip.js';
import { OverlayKindIcon } from './OverlayKindIcon.js';
import { addedAnnouncement, layerName, listPositionOf } from './overlayLayers.js';

export interface OverlayLayerListProps {
  elements: readonly OverlayElement[];
  /** Stable per-row keys, so a moved row keeps its DOM nodes (and focus). */
  rowIds: readonly string[];
  selectedId: string;
  onSelect: (id: string) => void;
  /** `delta` is in ordinal space: +1 brings the element forward, -1 sends it back. */
  onMove: (ordinal: number, delta: 1 | -1) => void;
  onRemove: (ordinal: number) => void;
  onAdd: (kind: OverlayElement['kind']) => void;
  canAdd: boolean;
  canRemove: boolean;
  readOnly?: boolean;
}

/** Spec 441. The layers of an overlay, front-most first. Redux-free. */
export function OverlayLayerList({
  elements,
  rowIds,
  selectedId,
  onSelect,
  onMove,
  onRemove,
  onAdd,
  canAdd,
  canRemove,
  readOnly = false,
}: OverlayLayerListProps) {
  const count = elements.length;
  // Same keyed-span pattern as OverlayEditor's undo region: a repeated message
  // still mutates the DOM, so it is announced again.
  const [announcement, setAnnouncement] = useState({ text: '', token: 0 });

  // A removed row takes the focused button with it, and a new row is not focused
  // by itself: after either, focus moves to the chip of the row now selected.
  const chips = useRef(new Map<string, HTMLButtonElement>());
  const pendingFocusCount = useRef<number | null>(null);
  useEffect(() => {
    if (pendingFocusCount.current === null || pendingFocusCount.current === count) return;
    pendingFocusCount.current = null;
    chips.current.get(selectedId)?.focus();
  });

  function announce(text: string) {
    setAnnouncement((previous) => ({ text, token: previous.token + 1 }));
  }

  function move(ordinal: number, delta: 1 | -1) {
    if (readOnly) return;
    const target = ordinal + delta;
    if (target < 0 || target >= count) return;
    const name = layerName(elements[ordinal]!, ordinal, elements);
    announce(`${name} moved to position ${listPositionOf(target, count)} of ${count}`);
    onMove(ordinal, delta);
  }

  function remove(ordinal: number) {
    if (readOnly || !canRemove) return;
    announce(`${layerName(elements[ordinal]!, ordinal, elements)} removed`);
    pendingFocusCount.current = count;
    onRemove(ordinal);
  }

  function add(kind: OverlayElement['kind']) {
    if (readOnly || !canAdd) return;
    announce(addedAnnouncement(kind, count + 1));
    pendingFocusCount.current = count;
    onAdd(kind);
  }

  const ordinals = elements.map((_, ordinal) => ordinal).reverse();

  return (
    <div className="flex flex-col gap-2">
      <ol aria-label="Layers" className="flex flex-col gap-1">
        {ordinals.map((ordinal) => {
          const element = elements[ordinal]!;
          const id = rowIds[ordinal]!;
          const name = layerName(element, ordinal, elements);
          return (
            <li key={id} className="flex items-center gap-2">
              <div role="group" aria-label={`Layer ${name}`} className="flex min-w-0 items-center gap-2">
                <ToggleChip
                  ref={(node) => {
                    if (node === null) chips.current.delete(id);
                    else chips.current.set(id, node);
                  }}
                  pressed={id === selectedId}
                  onClick={() => onSelect(id)}
                  className="max-w-full truncate"
                >
                  <OverlayKindIcon kind={element.kind} />
                  {name}
                </ToggleChip>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Move ${name} up`}
                  unavailable={readOnly || ordinal === count - 1}
                  onClick={() => move(ordinal, 1)}
                >
                  Move up
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Move ${name} down`}
                  unavailable={readOnly || ordinal === 0}
                  onClick={() => move(ordinal, -1)}
                >
                  Move down
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Remove ${name}`}
                  unavailable={readOnly || !canRemove}
                  onClick={() => remove(ordinal)}
                >
                  Remove
                </Button>
              </div>
            </li>
          );
        })}
      </ol>
      <div className="flex items-center gap-2">
        {readOnly || !canAdd ? (
          <Button type="button" variant="secondary" unavailable>
            Add
          </Button>
        ) : (
          <DropdownMenu
            align="start"
            trigger={
              <Button type="button" variant="secondary">
                Add
              </Button>
            }
            entries={[
              { kind: 'item', label: 'Text', onSelect: () => add('Text') },
              { kind: 'item', label: 'Box', onSelect: () => add('Box') },
              { kind: 'item', label: 'Ellipse', onSelect: () => add('Ellipse') },
            ]}
          />
        )}
        {!canAdd && <span className="text-sm text-fg-muted">Up to {MAX_ELEMENTS} elements</span>}
      </div>
      <div aria-live="polite" data-testid="overlay-layer-list-live-region" className="sr-only">
        <span key={announcement.token}>{announcement.text}</span>
      </div>
    </div>
  );
}
