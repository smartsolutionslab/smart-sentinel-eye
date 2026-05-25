import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { RegisterCameraDialog } from './RegisterCameraDialog.js';

export function CamerasPage() {
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Cameras</h1>
        <Button onClick={() => setDialogOpen(true)}>Register camera</Button>
      </header>
      <p className="text-fg-muted">List of registered cameras lands in the next PR.</p>
      <RegisterCameraDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
