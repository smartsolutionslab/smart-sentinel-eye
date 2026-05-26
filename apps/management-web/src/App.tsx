import { useState, type ReactNode } from 'react';
import { CamerasPage } from './features/cameras/CamerasPage.js';
import { LayoutsPage } from './features/layouts/LayoutsPage.js';

type View = 'cameras' | 'layouts';

// Placeholder shell for the management app. A real router lands when more
// than two surfaces exist; for spec 003 we toggle between the cameras and
// layouts pages so the nav remains visible everywhere.
export function App() {
  const [view, setView] = useState<View>('cameras');

  return (
    <main className="min-h-screen bg-bg-base text-fg-primary">
      <nav className="flex items-center gap-3 border-b border-fg-muted/30 px-6 py-3">
        <NavButton active={view === 'cameras'} onClick={() => setView('cameras')}>
          Cameras
        </NavButton>
        <NavButton active={view === 'layouts'} onClick={() => setView('layouts')}>
          Layouts
        </NavButton>
      </nav>
      {view === 'cameras' ? <CamerasPage /> : <LayoutsPage />}
    </main>
  );
}

function NavButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        active
          ? 'rounded-md bg-accent-active/10 px-3 py-1 text-sm font-medium text-accent-active'
          : 'rounded-md px-3 py-1 text-sm text-fg-muted hover:text-fg-primary'
      }
    >
      {children}
    </button>
  );
}
