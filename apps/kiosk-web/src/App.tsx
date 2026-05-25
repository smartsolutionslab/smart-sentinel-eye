// Placeholder shell for the kiosk display. Real layout renderer + overlay
// engine land per ADR-0074 / ADR-0076 once the first feature spec is written.
export function App() {
  return (
    <main className="min-h-screen flex items-center justify-center">
      <div className="text-center">
        <h1 className="text-3xl font-semibold">Smart Sentinel Eye</h1>
        <p className="mt-2 text-fg-muted">Kiosk — scaffold.</p>
      </div>
    </main>
  );
}
