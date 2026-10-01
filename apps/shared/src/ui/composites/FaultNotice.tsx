import type { ReactNode } from 'react';

export interface FaultNoticeProps {
  children: ReactNode;
}

// Signature-only stub (spec 298 / tasks.md T005): the phase-4b engineer fills
// this in to render the fault box (plan.md §2). Present only so FaultNotice's
// red test fails on missing behaviour, not on a missing module.
export function FaultNotice(_props: FaultNoticeProps) {
  return null;
}
