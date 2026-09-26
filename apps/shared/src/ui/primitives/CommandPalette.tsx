export interface CommandPaletteItem {
  /** Unique; handed back to `onSelect`. */
  value: string;
  /** Shown, and matched by the filter. */
  label: string;
}

export interface CommandPaletteProps {
  /** Controlled — the consumer owns the chord and the trigger. */
  open: boolean;
  onOpenChange: (open: boolean) => void;
  items: readonly CommandPaletteItem[];
  /** Called after the dialog has closed. */
  onSelect: (value: string) => void;
  /** Dialog title (sr-only) and the listbox's accessible name. */
  label: string;
  /** The search field's placeholder. */
  placeholder: string;
  /** Shown when the filter matches nothing. */
  emptyText: string;
}

/**
 * Spec 266 (issue #2335) phase 4a signature-only stub — plan.md §3.5 and §6.
 *
 * Renders nothing and imports no Radix package, so `CommandPalette.test.tsx`
 * fails on content, not on a missing module (ADR-0139/0144). Unlike US1–US4,
 * this stub's absence of a `@radix-ui/react-dialog` import does not affect
 * the US5 dependency guard either way — `Dialog.tsx` already imports it.
 */
export function CommandPalette(_props: CommandPaletteProps) {
  return null;
}
