import type { ComponentRef, ReactNode, Ref } from 'react';

export interface PageHeadingProps {
  ref: Ref<ComponentRef<'h1'>>;
  children: ReactNode;
}

// `tabIndex={-1}` makes the heading focusable by script only: `useHeadingFocus` moves
// focus here when a failed read recovers, without adding a Tab stop.
export function PageHeading({ ref, children }: PageHeadingProps) {
  return (
    <h1 ref={ref} tabIndex={-1} className="text-2xl font-semibold focus:outline-none">
      {children}
    </h1>
  );
}
