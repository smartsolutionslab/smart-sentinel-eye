import { ConfirmDialog } from '@smart-sentinel-eye/shared/ui/primitives/ConfirmDialog';
import type { ReactNode } from 'react';

export interface ArchiveConfirmationProps {
  /** The subject pending confirmation, or `null` when nothing is. */
  subject: string | null;
  onCancel: () => void;
  /**
   * The consequences of archiving *this* subject. Deliberately supplied by the
   * caller: the four differ, and a shared sentence would be true of all of them
   * and useful about none.
   */
  children: ReactNode;
  /** True while the archive request is in flight. */
  pending?: boolean;
  onConfirm: () => void;
}

/**
 * Asks before archiving (spec 036 FR-001).
 *
 * <p>
 * <b>One component for four callers</b>, which is the inverse of spec 035's
 * decision and for the inverse reason. There, two dialogs shared a <i>shape</i>
 * — a form, a schema, a mutation — while their behaviours differed, and
 * extraction was rejected. Here the behaviour is identical in all four (ask,
 * archive on confirm, nothing on dismiss) and only the words differ. Words are
 * props.
 * </p>
 *
 * <p>
 * FR-011 requires the sharing rather than merely permitting it: four copies
 * would be four places for dismiss-does-nothing to drift, and this feature
 * exists because something drifted.
 * </p>
 *
 * <p>
 * The behaviour underneath is {@link ConfirmDialog}, built shared by spec 032
 * for exactly this second caller — <c>role="alertdialog"</c>, focus defaulting
 * to cancel, and a <c>pending</c> guard against a second submit. Nothing here
 * re-implements any of it.
 * </p>
 */
export function ArchiveConfirmation({
  subject,
  onCancel,
  children,
  pending = false,
  onConfirm,
}: ArchiveConfirmationProps) {
  return (
    <ConfirmDialog
      // Open exactly when a subject is pending. Every page using this already
      // holds another open-state — an editor dialog, a dry-run panel — and a
      // nullable subject makes the two impossible to confuse while carrying the
      // name the wording needs.
      open={subject !== null}
      onOpenChange={(next) => {
        if (!next) {
          onCancel();
        }
      }}
      title={`Archive ${subject ?? ''}?`}
      confirmLabel="Archive"
      // Passed straight through rather than shadowed by a local flag: the
      // primitive already refuses the second click, and re-implementing that
      // here would give four callers a second guard to get wrong.
      pending={pending}
      onConfirm={onConfirm}
    >
      {children}
    </ConfirmDialog>
  );
}
