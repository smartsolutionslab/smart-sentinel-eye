import { CamerasPage } from './features/cameras/CamerasPage.js';

// Placeholder shell for the management app. Routing + persona-aware layout
// lands per ADR-0074 once additional features arrive; for now the cameras
// page is the single surface.
export function App() {
  return (
    <main className="min-h-screen bg-bg-base text-fg-primary">
      <CamerasPage />
    </main>
  );
}
