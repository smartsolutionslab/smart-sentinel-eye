import * as RadixAlertDialog from '@radix-ui/react-alert-dialog';
import type { ReactNode } from 'react';
import { Button } from './Button.js';

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  /** What confirming will do. Described in the future tense — see below. */
  children: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  /** True while the confirmed action is in flight. Blocks a second submit. */
  pending?: boolean;
  onConfirm: () => void;
}

/**
 * A confirmation for an action that cannot be taken back.
 *
 * Built on Radix's **alert** dialog rather than the plain {@link Dialog} this
 * file sits beside, because the two differ in the ways that matter here:
 * `role="alertdialog"` is announced more assertively, and focus lands on the
 * **cancelling** action rather than the first focusable element. For an
 * irreversible operation that second difference is the gap between a stray
 * Enter dismissing and a stray Enter going through with it.
 *
 * <p>
 * The product had no destructive confirmation of any kind before this — rules,
 * overlays, layouts and system variables all archive on a single click and ask
 * nothing. This is deliberately shared rather than living next to its one
 * caller, so the second one copies it instead of diverging from it.
 * </p>
 *
 * Callers should word the body as what confirming **will do**, not as what has
 * happened. A caller whose operation is idempotent cannot know afterwards
 * whether it was the one that caused the change.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  children,
  confirmLabel,
  cancelLabel = 'Cancel',
  pending = false,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <RadixAlertDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixAlertDialog.Portal>
        <RadixAlertDialog.Overlay className="fixed inset-0 bg-black/60 backdrop-blur-sm" />
        <RadixAlertDialog.Content
          className={
            'fixed left-1/2 top-1/2 w-full max-w-md -translate-x-1/2 -translate-y-1/2 ' +
            'max-h-[90vh] overflow-y-auto ' +
            'rounded-lg bg-bg-elevated p-6 shadow-xl border border-fg-muted text-fg-primary'
          }
        >
          <RadixAlertDialog.Title className="text-lg font-semibold">{title}</RadixAlertDialog.Title>
          <RadixAlertDialog.Description asChild>
            <div className="mt-2 space-y-2 text-sm text-fg-muted">{children}</div>
          </RadixAlertDialog.Description>
          <div className="mt-6 flex justify-end gap-2">
            <RadixAlertDialog.Cancel asChild>
              <Button variant="secondary" disabled={pending}>
                {cancelLabel}
              </Button>
            </RadixAlertDialog.Cancel>
            {/*
              Not wrapped in RadixAlertDialog.Action: Action closes the dialog
              on click, which would tear down the pending state the confirming
              control uses to refuse a second submit. The caller closes it when
              the request settles.
            */}
            <Button variant="danger" disabled={pending} onClick={onConfirm}>
              {confirmLabel}
            </Button>
          </div>
        </RadixAlertDialog.Content>
      </RadixAlertDialog.Portal>
    </RadixAlertDialog.Root>
  );
}
