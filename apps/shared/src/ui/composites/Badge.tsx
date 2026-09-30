import { Slot } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { ComponentPropsWithRef } from 'react';

export type BadgeTone = 'active' | 'warning' | 'fault' | 'neutral';
export type BadgeSize = 'sm' | 'md';

export interface BadgeProps extends ComponentPropsWithRef<'span'> {
  tone: BadgeTone;
  size?: BadgeSize;
  asChild?: boolean;
}

const BASE = 'inline-flex items-center whitespace-nowrap rounded-md font-medium';

const SIZE: Record<BadgeSize, string> = {
  sm: 'px-2 py-0.5 text-xs',
  md: 'px-3 py-1 text-xs',
};

const TONE: Record<BadgeTone, string> = {
  active: 'bg-accent-active-subtle text-accent-active',
  warning: 'bg-accent-warning-subtle text-accent-warning',
  fault: 'bg-accent-fault-subtle text-accent-fault',
  neutral: 'bg-bg-raised text-fg-muted',
};

/**
 * The shared status-pill primitive (spec 297, issue #2635, plan.md §3). A
 * status chip, not an affordance — lives in `composites/`, beside
 * `RetryBanner`, the other status surface on a triad tint (spec §4.3).
 *
 * Every class here is deliberately opaque and flat (FR-004): no call-site
 * alpha modifier, border, shadow, blur, transition, animation, opacity or
 * ring — ADR-0146 disqualifies translucency over live video, and a status
 * chip is not an interactive control.
 */
export function Badge({ tone, size = 'sm', asChild, className, ...rest }: BadgeProps) {
  const Component = asChild ? Slot : 'span';

  return <Component className={clsx(BASE, SIZE[size], TONE[tone], className)} {...rest} />;
}
