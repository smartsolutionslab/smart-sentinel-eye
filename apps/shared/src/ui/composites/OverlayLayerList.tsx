import { useState } from 'react';
import type { OverlayElement } from '../../api/overlays.api.js';
import { MAX_ELEMENTS } from '../../api/overlays.schema.js';
import { Button } from '../primitives/Button.js';
import { ToggleChip } from '../primitives/ToggleChip.js';
import { layerName, listPositionOf } from './overlayLayers.js';

export interface OverlayLayerListProps {
  elements: readonly OverlayElement[];
  /** Stable per-row keys, so a moved row keeps its DOM nodes (and focus). */
  rowIds: readonly string[];
  selectedId: string;
  onSelect: (id: string) => void;
  /** `delta` is in ordinal space: +1 brings the element forward, -1 sends it back. */
  onMove: (ordinal: number, delta: 1 | -1) => void;
  onRemove: (ordinal: number) => void;
  onAddLabel: () => void;
  canAdd: boolean;
  canRemove: boolean;
}

/** Spec 441. The layers of an overlay, front-most first. Redux-free. */
export function OverlayLayerList({
  elements,
  rowIds,
  selectedId,
  onSelect,
  onMove,
  onRemove,
  onAddLabel,
  canAdd,
  canRemove,
}: OverlayLayerListProps) {
  const count = elements.length;
  // Same keyed-span pattern as OverlayEditor's undo region: a repeated message
  // still mutates the DOM, so it is announced again.
  const [announcement, setAnnouncement] = useState({ text: '', token: 0 });

  function move(ordinal: number, delta: 1 | -1) {
    const target = ordinal + delta;
    if (target < 0 || target >= count) return;
    const name = layerName(elements[ordinal]!, ordinal, elements);
    setAnnouncement((previous) => ({
      text: `${name} moved to position ${listPositionOf(target, count)} of ${count}`,
      token: previous.token + 1,
    }));
    onMove(ordinal, delta);
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
              <ToggleChip pressed={id === selectedId} onClick={() => onSelect(id)} className="min-w-0 truncate">
                {name}
              </ToggleChip>
              <Button
                type="button"
                variant="secondary"
                aria-label={`Move ${name} up`}
                unavailable={ordinal === count - 1}
                onClick={() => move(ordinal, 1)}
              >
                Move up
              </Button>
              <Button
                type="button"
                variant="secondary"
                aria-label={`Move ${name} down`}
                unavailable={ordinal === 0}
                onClick={() => move(ordinal, -1)}
              >
                Move down
              </Button>
              <Button
                type="button"
                variant="secondary"
                aria-label={`Remove ${name}`}
                unavailable={!canRemove}
                onClick={() => {
                  if (canRemove) onRemove(ordinal);
                }}
              >
                Remove
              </Button>
            </li>
          );
        })}
      </ol>
      <div className="flex items-center gap-2">
        <Button
          type="button"
          variant="secondary"
          unavailable={!canAdd}
          onClick={() => {
            if (canAdd) onAddLabel();
          }}
        >
          Add label
        </Button>
        {!canAdd && <span className="text-sm text-fg-muted">Up to {MAX_ELEMENTS} elements</span>}
      </div>
      <div aria-live="polite" data-testid="overlay-layer-list-live-region" className="sr-only">
        <span key={announcement.token}>{announcement.text}</span>
      </div>
    </div>
  );
}
