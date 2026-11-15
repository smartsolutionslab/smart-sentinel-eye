import type { Page } from '@playwright/test';

export async function everyTileDecoded(page: Page, tiles: number): Promise<boolean> {
  return page.evaluate((expected) => {
    const videos = Array.from(document.querySelectorAll('video'));
    return videos.length === expected && videos.every((video) => video.getVideoPlaybackQuality().totalVideoFrames > 0);
  }, tiles);
}
