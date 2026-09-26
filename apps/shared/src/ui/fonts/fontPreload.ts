import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { HtmlTagDescriptor, Plugin, ResolvedConfig } from 'vite';

/**
 * Preloads the named files from this directory, at the URL the stylesheet
 * fetches them from (spec 261 plan.md §4.1, issue #2333).
 *
 * `configResolved` resolves each file's absolute path with
 * `new URL(file, import.meta.url)` — Vite's config bundler preserves each
 * imported module's own `import.meta.url`, so this always resolves beside
 * *this* file, not beside the caller's `vite.config.ts` — and throws if it
 * does not exist, so a typo'd filename fails the build rather than the wall.
 *
 * In dev (`serve`), the href is the `/@fs/` absolute path Vite's own CSS
 * rewriting gives this same file. In a build, the href is a path relative to
 * `config.root`; `order: 'pre'` puts the tag in before Vite's HTML asset
 * pass, which rewrites that relative href to the same hashed
 * `/assets/….woff2` URL the built CSS's `url()` gets, as one emitted asset.
 * `crossorigin` is mandatory — a `@font-face` `src` is always fetched in
 * anonymous-CORS mode, but a preload without `crossorigin` is fetched in
 * no-cors mode instead. The two modes don't share a cache key, so the font
 * fetch can't reuse the preloaded response: the browser fetches the same
 * bytes a second time, and the preload bought nothing.
 */
export function fontPreload(files: readonly string[]): Plugin {
  let config: ResolvedConfig;

  function resolvedFilePath(file: string): string {
    return fileURLToPath(new URL(file, import.meta.url));
  }

  return {
    name: 'sse-font-preload',
    configResolved(resolvedConfig) {
      config = resolvedConfig;

      for (const file of files) {
        const filePath = resolvedFilePath(file);
        if (!existsSync(filePath)) {
          throw new Error(`fontPreload: '${file}' does not exist beside fontPreload.ts (looked at ${filePath}).`);
        }
      }
    },
    transformIndexHtml: {
      order: 'pre',
      handler(): HtmlTagDescriptor[] {
        return files.map((file) => {
          const filePath = resolvedFilePath(file);
          const href =
            config.command === 'serve'
              ? `/@fs/${filePath.split(path.sep).join('/').replace(/^\//, '')}`
              : path.relative(config.root, filePath).split(path.sep).join('/');

          return {
            tag: 'link',
            attrs: { rel: 'preload', href, as: 'font', type: 'font/woff2', crossorigin: '' },
            injectTo: 'head',
          };
        });
      },
    },
  };
}
