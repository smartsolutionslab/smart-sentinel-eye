import {
  getOverlayMock,
  capturedCallbacks,
  tile,
  publishedRevision,
  mockLayout,
  renderPage,
  publishedOverlay,
  resetCellPageMocks,
} from '../../test/cellPageHarness';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { act, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { store } from '../../app/store';
import { CellPage } from './CellPage';

describe('CellPage', () => {
  beforeEach(resetCellPageMocks);

  describe('Archive and publish frames arriving out of order across overlays and revisions (#3275)', () => {
    afterEach(() => {
      store.dispatch(systemVariablesApi.util.resetApiState());
      store.dispatch(overlaysApi.util.resetApiState());
    });

    function wallOf(tiles: LayoutTile[]) {
      mockLayout(publishedRevision(1, tiles.length, tiles));
      getOverlayMock.mockReturnValue(publishedOverlay('Line label'));
    }

    function aWallWithTileABoundTo(overlayA: string, overlayB = 'ov-2') {
      wallOf([
        tile({ cameraIdentifier: 'cam-a', overlayIdentifier: overlayA, row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', overlayIdentifier: overlayB, row: 0, col: 1 }),
      ]);
    }

    function thePage() {
      return (
        <Provider store={store}>
          <MemoryRouter>
            <CellPage />
          </MemoryRouter>
        </Provider>
      );
    }

    function tiles() {
      return screen.getAllByTestId('layout-tile');
    }

    function unavailableBadgeIn(tileEl: HTMLElement) {
      return within(tileEl).queryByText(/overlay unavailable/i);
    }

    function publish(overlay: string, revisionNumber: number) {
      act(() => {
        capturedCallbacks?.onOverlayPublished?.({
          overlay,
          revisionNumber,
          name: 'Line label',
          elements: [
            {
              kind: 'Text',
              color: '#FFFFFFD9',
              text: 'Line label',
              normalizedX: 0.5,
              normalizedY: 0.05,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 48,
            },
          ],
          publishedAt: '2026-09-23T10:05:00Z',
        });
      });
    }

    function archive(overlay: string, revisionNumber: number) {
      act(() => {
        capturedCallbacks?.onOverlayArchived?.({ overlay, revisionNumber, archivedAt: '2026-09-23T10:06:00Z' });
      });
    }

    it("Flags another overlay's tile on its archive frame even when a higher revision of a different overlay was published", () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA, tileB] = tiles();

      publish('ov-1', 5);
      archive('ov-2', 1);

      expect(unavailableBadgeIn(tileB!), 'the publish mark belongs to ov-1 alone').toBeInTheDocument();
      expect(unavailableBadgeIn(tileA!), 'ov-1 was not archived').not.toBeInTheDocument();
    });

    it('Flags the tile whose overlay was never published even after the other overlay is republished out of order', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA, tileB] = tiles();

      publish('ov-2', 2);
      archive('ov-2', 1);
      archive('ov-1', 1);

      expect(unavailableBadgeIn(tileA!), 'ov-1 has no publish mark, so its archive is real').toBeInTheDocument();
      expect(unavailableBadgeIn(tileB!), "ov-2's stale archive is ignored").not.toBeInTheDocument();
    });

    it('Ignores a stale archive frame that arrives after a genuine archive and a later republish', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA] = tiles();

      publish('ov-1', 2);
      archive('ov-1', 2);
      expect(unavailableBadgeIn(tileA!), 'the genuine archive flags').toBeInTheDocument();

      publish('ov-1', 3);
      expect(unavailableBadgeIn(tileA!), 'the republish clears it').not.toBeInTheDocument();

      archive('ov-1', 2);
      expect(
        unavailableBadgeIn(tileA!),
        "a replay of revision 2's archive must not blank revision 3",
      ).not.toBeInTheDocument();
    });

    it('Still flags a genuine archive of the republished revision after a stale archive was ignored', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA] = tiles();

      publish('ov-1', 3);
      archive('ov-1', 2);
      archive('ov-1', 3);

      expect(unavailableBadgeIn(tileA!), 'ignoring one stale frame must not disarm the overlay').toBeInTheDocument();
    });

    it('Keeps the highest published revision when an older publish frame arrives late', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA] = tiles();

      publish('ov-1', 3);
      publish('ov-1', 2);
      archive('ov-1', 2);

      expect(unavailableBadgeIn(tileA!), 'the mark is the maximum seen, not the last seen').not.toBeInTheDocument();
    });

    it('Ignores a stale archive frame after the same publish frame was delivered twice', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA] = tiles();

      publish('ov-1', 2);
      publish('ov-1', 2);
      archive('ov-1', 1);
      archive('ov-1', 1);

      expect(unavailableBadgeIn(tileA!), 'a duplicate frame changes nothing').not.toBeInTheDocument();
    });

    it('Clears the flag when the archive of the prior revision arrives before the publish of the new one', () => {
      aWallWithTileABoundTo('ov-1');
      renderPage();
      const [tileA] = tiles();

      publish('ov-1', 1);
      archive('ov-1', 1);
      expect(unavailableBadgeIn(tileA!), 'the archive arriving first still flags').toBeInTheDocument();

      publish('ov-1', 2);
      expect(unavailableBadgeIn(tileA!), 'the newer publish clears it').not.toBeInTheDocument();
    });

    it('Ignores a stale archive frame on every tile that binds the republished overlay', () => {
      wallOf([
        tile({ cameraIdentifier: 'cam-a', overlayIdentifier: 'ov-1', row: 0, col: 0 }),
        tile({ cameraIdentifier: 'cam-b', overlayIdentifier: 'ov-1', row: 0, col: 1 }),
      ]);
      renderPage();
      const [tileA, tileB] = tiles();

      publish('ov-1', 2);
      archive('ov-1', 1);

      expect(unavailableBadgeIn(tileA!), 'first tile binding ov-1').not.toBeInTheDocument();
      expect(unavailableBadgeIn(tileB!), 'second tile binding ov-1').not.toBeInTheDocument();
    });

    it("Flags a rebound tile on its new overlay's archive frame regardless of the old overlay's publish mark", () => {
      aWallWithTileABoundTo('ov-1');
      const view = renderPage();

      publish('ov-1', 5);

      aWallWithTileABoundTo('ov-3');
      view.rerender(thePage());
      const [tileA] = tiles();

      archive('ov-3', 1);

      expect(unavailableBadgeIn(tileA!), 'the publish mark is per overlay, not per tile').toBeInTheDocument();
    });

    it('Ignores a stale archive frame for an overlay unbound and rebound since its newer revision was published', () => {
      aWallWithTileABoundTo('ov-1');
      const view = renderPage();

      publish('ov-1', 2);

      aWallWithTileABoundTo('ov-3');
      view.rerender(thePage());
      aWallWithTileABoundTo('ov-1');
      view.rerender(thePage());
      const [tileA] = tiles();

      archive('ov-1', 1);

      expect(unavailableBadgeIn(tileA!), 'revision 2 is still the newest published').not.toBeInTheDocument();
    });

    it('Does not carry a publish mark into a freshly mounted page', () => {
      aWallWithTileABoundTo('ov-1');
      const first = renderPage();
      publish('ov-1', 2);
      first.unmount();

      renderPage();
      const [tileA] = tiles();
      archive('ov-1', 1);

      expect(unavailableBadgeIn(tileA!), 'a new page starts with no marks').toBeInTheDocument();
    });
  });
});
