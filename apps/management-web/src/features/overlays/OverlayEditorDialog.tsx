import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { OverlayDraftForm, type OverlayEditTarget } from './OverlayDraftForm.js';

export type { OverlayEditTarget };

export interface OverlayEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** When set the dialog edits an existing draft (spec 152); otherwise it creates. */
  editTarget?: OverlayEditTarget;
}

/**
 * Spec 305 (#2350), plan.md "Shape of the change". A thin wrapper: the
 * editing behaviour — the form, the mutations, the chain read, the save
 * gate — lives in `OverlayDraftForm`, extracted verbatim so the suites that
 * pin it (now hosted on the form directly) could be captured green across
 * the extraction, unmodified.
 *
 * Deliberately no `reset(defaultValues)` / mutation-`reset()` call here on
 * close: `Dialog`'s own Radix `Presence` unmounts `OverlayDraftForm` (and
 * everything inside it, including its mutation hooks) the moment `open`
 * goes false, which is what the WHEP-teardown tests already prove — so the
 * form's own cleanup-on-unmount effect is what clears a refused save's
 * banner between opens, not an effect living here.
 *
 * Retired entirely at T012 once both routed pages exist — `OverlaysPage.tsx`
 * stops mounting this and navigates instead.
 */
export function OverlayEditorDialog({ open, onOpenChange, editTarget }: OverlayEditorDialogProps) {
  const isEdit = editTarget !== undefined;

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Edit overlay draft' : 'New overlay'}
      description={
        isEdit
          ? `Editing draft v${editTarget.revisionNumber} of ${editTarget.name}. The change is saved onto this draft.`
          : 'Pick a name, type the label, and drag it to position. The overlay starts as a draft.'
      }
    >
      <OverlayDraftForm
        editTarget={editTarget}
        onDone={() => onOpenChange(false)}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
}
