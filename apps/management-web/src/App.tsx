import { useState, type ReactNode } from 'react';
import { CamerasPage } from './features/cameras/CamerasPage.js';
import { LayoutsPage } from './features/layouts/LayoutsPage.js';
import { OverlaysPage } from './features/overlays/OverlaysPage.js';
import { SystemVariablesPage } from './features/systemVariables/SystemVariablesPage.js';

type View = 'cameras' | 'layouts' | 'overlays' | 'system-variables';

// Placeholder shell for the management app. A real router lands when more
// than three surfaces exist; for spec 004 we toggle between cameras,
// layouts, and overlays so the nav remains visible everywhere.
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
        <NavButton active={view === 'overlays'} onClick={() => setView('overlays')}>
          Overlays
        </NavButton>
        <NavButton active={view === 'system-variables'} onClick={() => setView('system-variables')}>
          System variables
        </NavButton>
      </nav>
      {view === 'cameras' && <CamerasPage />}
      {view === 'layouts' && <LayoutsPage />}
      {view === 'overlays' && <OverlaysPage />}
      {view === 'system-variables' && <SystemVariablesPage />}
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
