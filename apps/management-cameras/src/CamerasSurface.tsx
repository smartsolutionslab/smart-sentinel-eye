import { Route, Routes } from 'react-router-dom';
import './styles/remote.css';
import { CameraDetailPage } from './features/cameras/CameraDetailPage.js';
import { CamerasPage } from './features/cameras/CamerasPage.js';

/**
 * The cameras remote's exposed module (plan 316 §2.2, §5.1, §5.3), loaded by
 * the shell through `@module-federation/runtime`'s
 * `loadRemote('cameras/CamerasSurface')` behind `RemoteSurface`
 * (`apps/management-web/src/app/navigation/RemoteSurface.tsx`).
 *
 * <p>
 * Static imports, no nested `lazy`: the whole remote arrives on the first
 * navigation to `/cameras`, so the click-to-first-frame budget measured from
 * the list to a detail page (spec 002 FR-013, `click-to-first-frame.spec.ts`)
 * happens after the remote is already loaded and incurs no further fetch
 * (plan §5.3).
 * </p>
 *
 * <p>
 * Descendant `<Routes>`, matched against whatever remainder the shell's
 * `cameras/*` route hands it — `index` for `/cameras` itself,
 * `:cameraIdentifier` for `/cameras/:cameraIdentifier`, mirroring the two
 * routes `router.tsx` used to declare directly (FR-002).
 * </p>
 */
export default function CamerasSurface() {
  return (
    <Routes>
      <Route index element={<CamerasPage />} />
      <Route path=":cameraIdentifier" element={<CameraDetailPage />} />
    </Routes>
  );
}
