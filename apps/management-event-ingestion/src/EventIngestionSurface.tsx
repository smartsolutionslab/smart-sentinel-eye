import { Route, Routes } from 'react-router-dom';
import './styles/remote.css';
import { HeldEventTypesPage } from './features/eventTypes/HeldEventTypesPage.js';

/**
 * The event-ingestion remote's exposed module (issue #2780), loaded by the
 * shell through `@module-federation/runtime`'s
 * `loadRemote('eventIngestion/EventIngestionSurface')` behind
 * `RemoteSurface` — mirrors `apps/management-cameras/src/CamerasSurface.tsx`
 * exactly, including its static-import, no-nested-`lazy` reasoning: the
 * whole remote arrives on the first navigation to `/event-ingestion`.
 */
export default function EventIngestionSurface() {
  return (
    <Routes>
      <Route index element={<HeldEventTypesPage />} />
    </Routes>
  );
}
