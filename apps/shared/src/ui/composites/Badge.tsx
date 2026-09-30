import type { ComponentPropsWithRef } from 'react';

export type BadgeTone = 'active' | 'warning' | 'fault' | 'neutral';
export type BadgeSize = 'sm' | 'md';

export interface BadgeProps extends ComponentPropsWithRef<'span'> {
  tone: BadgeTone;
  size?: BadgeSize;
  asChild?: boolean;
}

/**
 * Signature-only stub (spec 297, issue #2635, plan.md §6). `tsc --noEmit`
 * green; `Badge.test.tsx` red on class content and on `asChild` element
 * shape — the real implementation (tone/size classes, Slot for `asChild`)
 * is phase 4b's (plan.md §3).
 */
export function Badge({ tone: _tone, size: _size, asChild: _asChild, children, ...rest }: BadgeProps) {
  return <span {...rest}>{children}</span>;
}
