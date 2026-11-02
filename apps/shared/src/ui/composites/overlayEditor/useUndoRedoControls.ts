import { useCallback, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';

export function useUndoRedoControls({ undo, redo }: { undo: () => boolean; redo: () => boolean }) {
  // Spec 154 (issue #2347) US2 — a discrete act, not a burst, so no debounce
  // (unlike `useGeometryAnnouncer`'s `queueAnnouncement`). Phase 6 review finding (#2344's
  // defect, twenty lines above its own fix): a same-string `setState` is a
  // React no-op — no DOM write — so a screen reader never re-announces a
  // repeated outcome, and `Ctrl+Z` pressed twice at the floor is exactly the
  // likeliest repeat ('Nothing to undo', twice). `queueAnnouncement`'s own
  // fix (`lastAnnouncedRef` + a toggled trailing zero-width space) does not
  // reuse cleanly here: its *visible* output alternates between "message"
  // and "message<ZWSP>" on repeats, harmless for a screen reader but not
  // byte-identical text on every call, and this announcer has no debounce
  // window to key a "was the last one the same" comparison against in the
  // first place. `token`, bumped on every call and used as the inner
  // `<span>`'s React `key`, forces a fresh DOM node on every announcement
  // instead — a real mutation even for back-to-back identical text, with no
  // leftover character.
  const [undoAnnouncement, setUndoAnnouncement] = useState({ text: '', token: 0 });

  const announceUndo = useCallback((message: string) => {
    setUndoAnnouncement((prev) => ({ text: message, token: prev.token + 1 }));
  }, []);

  const handleUndoClick = useCallback(() => {
    announceUndo(undo() ? 'Undone' : 'Nothing to undo');
  }, [undo, announceUndo]);

  const handleRedoClick = useCallback(() => {
    announceUndo(redo() ? 'Redone' : 'Nothing to redo');
  }, [redo, announceUndo]);

  // FR-006/FR-007: bound on the editor's root element, not the label, so
  // `Ctrl+Z`/`Cmd+Z`/`Ctrl+Shift+Z`/`Cmd+Shift+Z`/`Ctrl+Y` work from any
  // focus position inside the editor. `preventDefault` only on a handled
  // combination; everything else — `Ctrl+C`, `Ctrl+V`, the browser's own
  // shortcuts — falls through untouched.
  const handleRootKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (!event.ctrlKey && !event.metaKey) return;
      const key = event.key.toLowerCase();
      if (key === 'z' && event.shiftKey) {
        event.preventDefault();
        handleRedoClick();
        return;
      }
      if (key === 'z') {
        event.preventDefault();
        handleUndoClick();
        return;
      }
      if (key === 'y' && event.ctrlKey) {
        event.preventDefault();
        handleRedoClick();
      }
    },
    [handleUndoClick, handleRedoClick],
  );

  return { undoAnnouncement, handleUndoClick, handleRedoClick, handleRootKeyDown };
}
