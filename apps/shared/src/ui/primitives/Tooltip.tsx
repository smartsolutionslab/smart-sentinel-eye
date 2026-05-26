import * as RadixTooltip from '@radix-ui/react-tooltip';
import clsx from 'clsx';
import type { ReactNode } from 'react';

export interface TooltipProps {
  trigger: ReactNode;
  content: ReactNode;
  side?: 'top' | 'right' | 'bottom' | 'left';
  delayMs?: number;
  contentClassName?: string;
}

/**
 * Headless Radix tooltip wrapped with the project's design-token classes.
 * Tooltip.Provider is included so callers don't need to remember to wrap
 * each instance; nesting providers is harmless.
 */
export function Tooltip({
  trigger,
  content,
  side = 'top',
  delayMs = 200,
  contentClassName,
}: TooltipProps) {
  return (
    <RadixTooltip.Provider delayDuration={delayMs}>
      <RadixTooltip.Root>
        <RadixTooltip.Trigger asChild>{trigger}</RadixTooltip.Trigger>
        <RadixTooltip.Portal>
          <RadixTooltip.Content
            side={side}
            sideOffset={4}
            className={clsx(
              'rounded-md border border-fg-muted/40 bg-bg-elevated px-2 py-1 text-xs text-fg-primary shadow-md whitespace-pre',
              contentClassName,
            )}
          >
            {content}
            <RadixTooltip.Arrow className="fill-bg-elevated" />
          </RadixTooltip.Content>
        </RadixTooltip.Portal>
      </RadixTooltip.Root>
    </RadixTooltip.Provider>
  );
}
