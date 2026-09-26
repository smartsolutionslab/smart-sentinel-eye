// @vitest-environment node
//
// Runs this app's REAL `vite build` (root = app root, this app's own
// vite.config.ts) into a fresh mkdtemp directory — not a mock, the actual
// bundling `pnpm build` runs — and inspects the built CSS/HTML (spec 261,
// issue #2333, plan.md §6.3). One per app (kiosk-web here), for the reason
// spec 257 plan.md §5.3 gives: two configs, two chances to wire it wrong.
// Identical to management-web's copy except for this comment and the
// describe title.
//
// Red on develop (spec 261 §6): B1, B2, B4, B5 — nothing here declares a
// Plex font yet. B3 is a declared vacuous-green pin (today's build reaches
// for no absolute URL either) — proved live by a planted counterfactual
// (plan.md §6.5 #1), not by this test going red on develop.
//
// R2 (plan.md §10) named a fallback for the case where programmatic
// `build()` cannot run inside the vitest worker (nested Vite). It has never
// been needed — programmatic `build()` runs fine here — and there is no
// fallback in this file: a failure surfaces as `build()`'s own thrown error.
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import postcss from 'postcss';

const stylesDir = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(stylesDir, '..', '..');
const fontsDir = path.resolve(appRoot, '..', 'shared', 'src', 'ui', 'fonts');

// The twelve files plan.md §1 names, copied unmodified from IBM's own
// packages (T010, phase 4b). Not yet present — B2 is red until they land.
const PLEX_FILES = [
  'IBMPlexSans-Regular-Latin1.woff2',
  'IBMPlexSans-Regular-Latin2.woff2',
  'IBMPlexSans-Regular-Pi.woff2',
  'IBMPlexSans-Medium-Latin1.woff2',
  'IBMPlexSans-Medium-Latin2.woff2',
  'IBMPlexSans-Medium-Pi.woff2',
  'IBMPlexSans-SemiBold-Latin1.woff2',
  'IBMPlexSans-SemiBold-Latin2.woff2',
  'IBMPlexSans-SemiBold-Pi.woff2',
  'IBMPlexMono-Regular-Latin1.woff2',
  'IBMPlexMono-Regular-Latin2.woff2',
  'IBMPlexMono-Regular-Pi.woff2',
];

interface BuiltFontFace {
  family: string;
  weight: string;
  unicodeRange: string | undefined;
  urls: string[];
}

let outDir: string;
let indexHtml: string;
let builtCss: string;

beforeAll(async () => {
  outDir = mkdtempSync(path.join(tmpdir(), 'sse-fonts-build-'));

  const viteModule = await import('vite');
  await viteModule.build({
    root: appRoot,
    logLevel: 'silent',
    build: { outDir, emptyOutDir: true, write: true },
  });

  const indexHtmlPath = path.join(outDir, 'index.html');
  indexHtml = existsSync(indexHtmlPath) ? readFileSync(indexHtmlPath, 'utf8') : '';

  const assetsDir = path.join(outDir, 'assets');
  const cssFiles = existsSync(assetsDir) ? readdirSync(assetsDir).filter((file) => file.endsWith('.css')) : [];
  builtCss = cssFiles.map((file) => readFileSync(path.join(assetsDir, file), 'utf8')).join('\n');
}, 180_000);

afterAll(() => {
  if (outDir !== undefined && existsSync(outDir)) {
    rmSync(outDir, { recursive: true, force: true });
  }
});

function parseBuiltFontFaces(css: string): BuiltFontFace[] {
  const root = postcss.parse(css);
  const faces: BuiltFontFace[] = [];

  root.walkAtRules('font-face', (atRule) => {
    let family = '';
    let weight = '';
    let unicodeRange: string | undefined;
    let srcValue = '';

    atRule.walkDecls((decl) => {
      if (decl.prop === 'font-family') {
        family = decl.value.trim().replace(/^['"]|['"]$/g, '');
      } else if (decl.prop === 'font-weight') {
        weight = decl.value.trim();
      } else if (decl.prop === 'unicode-range') {
        unicodeRange = decl.value.trim();
      } else if (decl.prop === 'src') {
        srcValue = decl.value.trim();
      }
    });

    const urls = [...srcValue.matchAll(/url\((['"]?)([^'")]*)\1\)/g)].map((match) => match[2] ?? '');
    faces.push({ family, weight, unicodeRange, urls });
  });

  return faces;
}

function customPropertyValue(css: string, name: string): string | undefined {
  const root = postcss.parse(css);
  let value: string | undefined;
  root.walkDecls(name, (decl) => {
    value ??= decl.value.trim();
  });
  return value;
}

function unicodeRangeContains(spec: string, codePoint: number): boolean {
  for (const rawToken of spec.split(',')) {
    const token = rawToken.trim().replace(/^U\+/i, '');
    if (token.length === 0) {
      continue;
    }
    if (token.includes('-')) {
      const [start, end] = token.split('-');
      if (codePoint >= parseInt(start!, 16) && codePoint <= parseInt(end!, 16)) {
        return true;
      }
    } else if (parseInt(token, 16) === codePoint) {
      return true;
    }
  }
  return false;
}

function preloadFontLinks(html: string): Array<{ href: string; type: string | undefined; crossorigin: boolean }> {
  const links: Array<{ href: string; type: string | undefined; crossorigin: boolean }> = [];

  for (const match of html.matchAll(/<link\b[^>]*>/gi)) {
    const tag = match[0];
    if (!/rel\s*=\s*["']preload["']/i.test(tag) || !/as\s*=\s*["']font["']/i.test(tag)) {
      continue;
    }

    const href = tag.match(/href\s*=\s*["']([^"']+)["']/i)?.[1] ?? '';
    const type = tag.match(/type\s*=\s*["']([^"']+)["']/i)?.[1];
    links.push({ href, type, crossorigin: /\bcrossorigin\b/i.test(tag) });
  }

  return links;
}

describe('kiosk-web built output self-hosts IBM Plex (spec 261 US1/US2/US3)', () => {
  it('the build emits at least 12 @font-face rules, each url() a same-origin /assets/*.woff2 that exists (B1)', () => {
    const faces = parseBuiltFontFaces(builtCss);

    expect(
      faces.length,
      `expected >= 12 @font-face rules in the built CSS, found ${faces.length}`,
    ).toBeGreaterThanOrEqual(12);

    const problems: string[] = [];
    for (const face of faces) {
      for (const url of face.urls) {
        if (!/^\/assets\/[^/]+\.woff2$/.test(url)) {
          problems.push(`${face.family} ${face.weight}: url(${url}) is not a root-relative /assets/*.woff2 path`);
          continue;
        }
        if (!existsSync(path.join(outDir, url.slice(1)))) {
          problems.push(`${face.family} ${face.weight}: url(${url}) names a file that does not exist in outDir`);
        }
      }
    }

    expect(problems, problems.join('\n')).toEqual([]);
  });

  it('each of the 12 committed woff2 files is byte-identical to a file in the build output (B2)', () => {
    const assetsDir = path.join(outDir, 'assets');
    const builtBuffers = existsSync(assetsDir)
      ? readdirSync(assetsDir)
          .filter((file) => file.endsWith('.woff2'))
          .map((file) => readFileSync(path.join(assetsDir, file)))
      : [];

    const problems: string[] = [];
    for (const fileName of PLEX_FILES) {
      const sourcePath = path.join(fontsDir, fileName);
      if (!existsSync(sourcePath)) {
        problems.push(`${fileName} does not exist at ${sourcePath}.`);
        continue;
      }
      const sourceBytes = readFileSync(sourcePath);
      if (!builtBuffers.some((built) => built.equals(sourceBytes))) {
        problems.push(`${fileName} has no byte-identical match under ${assetsDir}.`);
      }
    }

    expect(problems, problems.join('\n')).toEqual([]);
  });

  it('no built .css or .html reaches for an absolute URL (B3, declared vacuous-green pin)', () => {
    const problems: string[] = [];
    const absolute = /(?:url\(|@import\s|href=|src=)\s*['"]?(https?:)?\/\//gi;

    for (const [label, text] of [
      ['built css', builtCss],
      ['built index.html', indexHtml],
    ] as const) {
      for (const match of text.matchAll(absolute)) {
        problems.push(`${label}: ${match[0]}`);
      }
    }

    expect(problems, problems.join('\n')).toEqual([]);
  });

  it('index.html preloads exactly the Sans 400 and 600 Latin1 faces, at the CSS’s own url() (B4)', () => {
    const faces = parseBuiltFontFaces(builtCss);
    const firstScreenFaces = faces.filter(
      (face) =>
        face.family === 'IBM Plex Sans' &&
        (face.weight === '400' || face.weight === '600') &&
        face.unicodeRange !== undefined &&
        unicodeRangeContains(face.unicodeRange, 0x41),
    );

    const links = preloadFontLinks(indexHtml);

    expect(links.length, `expected exactly 2 <link rel="preload" as="font">, found ${links.length}`).toBe(2);

    for (const link of links) {
      expect(link.type, `${link.href} has no type="font/woff2"`).toBe('font/woff2');
      expect(link.crossorigin, `${link.href} has no crossorigin attribute`).toBe(true);
    }

    const expectedHrefs = new Set(firstScreenFaces.flatMap((face) => face.urls));
    const actualHrefs = new Set(links.map((link) => link.href));

    expect(actualHrefs, `preload hrefs ${[...actualHrefs].join(', ')} must equal the Sans 400/600 url()s`).toEqual(
      expectedHrefs,
    );
  });

  it('--font-sans and --font-mono begin with the Plex families, then their fallback (B5)', () => {
    const fontSans = customPropertyValue(builtCss, '--font-sans');
    const fontMono = customPropertyValue(builtCss, '--font-mono');

    const normalize = (value: string | undefined) => (value ?? '').replace(/"/g, "'").replace(/\s+/g, ' ').trim();

    expect(normalize(fontSans), `--font-sans is ${fontSans}`).toMatch(/^'IBM Plex Sans',\s*'IBM Plex Sans Fallback',/);
    expect(normalize(fontMono), `--font-mono is ${fontMono}`).toMatch(/^'IBM Plex Mono',\s*'IBM Plex Mono Fallback',/);
  });
});
