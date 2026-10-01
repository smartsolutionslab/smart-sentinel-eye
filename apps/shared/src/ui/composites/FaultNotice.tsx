import type { ReactNode } from 'react';

export interface FaultNoticeProps {
  children: ReactNode;
}

export function FaultNotice({ children }: FaultNoticeProps) {
  return (
    <div
      role="alert"
      className="mb-4 rounded-md border border-accent-fault-border bg-accent-fault-subtle px-3 py-2 text-sm text-accent-fault"
    >
      {children}
    </div>
  );
}
