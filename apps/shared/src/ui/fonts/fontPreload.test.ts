/// <reference types="node" />
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import type { HtmlTagDescriptor, ResolvedConfig } from 'vite';
import { fontPreload } from './fontPreload';

const fontsDir = path.dirname(fileURLToPath(import.meta.url));
const font = 'IBMPlexSans-Regular-Latin1.woff2';
const fsPath = path.join(fontsDir, font).split(path.sep).join('/').replace(/^\//, '');

function preloadHrefs(config: Pick<ResolvedConfig, 'command' | 'base' | 'root'>): string[] {
  const plugin = fontPreload([font]);
  (plugin.configResolved as (resolved: ResolvedConfig) => void)(config as ResolvedConfig);
  const hook = plugin.transformIndexHtml as { handler: () => HtmlTagDescriptor[] };
  return hook.handler().map((tag) => String(tag.attrs?.['href']));
}

describe('The font preload points where the stylesheet fetches from', () => {
  it('Prefixes the dev server base so the preload is not a request outside the instance', () => {
    const hrefs = preloadHrefs({ command: 'serve', base: '/kiosk/', root: fontsDir });

    expect(hrefs).toEqual([`/kiosk/@fs/${fsPath}`]);
  });

  it('Leaves the dev href unchanged under the root base', () => {
    const hrefs = preloadHrefs({ command: 'serve', base: '/', root: fontsDir });

    expect(hrefs).toEqual([`/@fs/${fsPath}`]);
  });

  it.each(['/', '/kiosk/'])('Gives a build the same root-relative href under base %s', (base) => {
    const hrefs = preloadHrefs({ command: 'build', base, root: path.dirname(fontsDir) });

    expect(hrefs).toEqual([`fonts/${font}`]);
  });
});
