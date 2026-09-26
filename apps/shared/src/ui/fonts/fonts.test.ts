// Spec 261 (issue #2333), plan.md §6.2 facts S1-S5. Red on develop: fonts.css
// and the twelve committed woff2 files do not exist yet (plan.md §6). This
// file's own correctness is exercised for real once T010/T011 (phase 4b) land
// the bytes and the stylesheet; until then the assertions below are expected
// to fail on a missing file, which is the "red because absent" spec §6 calls
// out — not a weak red, because S2-S5's *logic* is unconditional on content
// once the files exist, and T008's counterfactual re-proves the two
// value-bearing facts (S1, S4) after 4b lands (plan.md §6.5 #4).
/// <reference types="node" />
import { Buffer } from 'node:buffer';
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import type { Dirent } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import postcss from 'postcss';
import ts from 'typescript';
import { createFontStack } from '@capsizecss/core';
import iBMPlexSans400 from '@capsizecss/metrics/iBMPlexSans';
import iBMPlexSans500 from '@capsizecss/metrics/iBMPlexSans/500';
import iBMPlexSans600 from '@capsizecss/metrics/iBMPlexSans/600';
import iBMPlexMono400 from '@capsizecss/metrics/iBMPlexMono';
import arial from '@capsizecss/metrics/arial';
import courierNew from '@capsizecss/metrics/courierNew';
import { parseWoff2 } from '../../test/woff2.js';

const fontsDir = path.dirname(fileURLToPath(import.meta.url));
const fontsCssPath = path.join(fontsDir, 'fonts.css');

// The bytes' provenance (plan.md §1, measured 2026-09-25). Literals, not read
// back from the subject — so the subject can change without the assertion
// changing (the standing lesson: an assertion must not check its own input).
const SHA256_BY_FILE: Record<string, string> = {
  'IBMPlexSans-Regular-Latin1.woff2': 'b5ad7bd39f996144915f0ad9849a90183b27d8c28ad97ed98af5b1bebc51f6b1',
  'IBMPlexSans-Regular-Latin2.woff2': 'fcfa82e0d46ed01a8df38610b451860ca7e7518770c52fdd2a1d136247c40f28',
  'IBMPlexSans-Regular-Pi.woff2': '1487059829a180f975627e473acc81ff22c2c0faf1da09b314c27eeb41b7f2e4',
  'IBMPlexSans-Medium-Latin1.woff2': 'b5610af04d0d4b5a14a621d96d974b993e945a065db1a8861918f69ef9321934',
  'IBMPlexSans-Medium-Latin2.woff2': '46fbe4f398e7ff3cc7773a1156b1efef0f81e4d034f682369c221de587f443ca',
  'IBMPlexSans-Medium-Pi.woff2': 'bf05f10c977353cfb5a5c11e8973adf77c2b93a4798da3aa0dd8ba5088e12515',
  'IBMPlexSans-SemiBold-Latin1.woff2': 'fff0ab3a88b0b4aa0b693e4f0201359a15183b08e3fa5696d1918d8f0ade8ad5',
  'IBMPlexSans-SemiBold-Latin2.woff2': '2bc85a8e53540c4c211f7af36e806bb045ba7da8946bb221d87144c12c83d577',
  'IBMPlexSans-SemiBold-Pi.woff2': '768421433d850d3a30118dddf05972625d99ee49bc32c5a8fd26bbe020c4d0f9',
  'IBMPlexMono-Regular-Latin1.woff2': 'e8993d946649b9d01abb1ed06d574b19d8ea3e66b5c3948602db335c44c18e56',
  'IBMPlexMono-Regular-Latin2.woff2': '8c5e48e4c6ee60346744db62fd8bbf530c21e531e1a1498c3a410ed8f7d3986e',
  'IBMPlexMono-Regular-Pi.woff2': 'b8002770aa636f544ba43e124da6a227301769754f295eae26e16475b469c767',
};
const OFL_SHA256 = '37784b44044a4ffd9256702b7c0982c37e5c8887ba90c6dca0479aea93dc898d';

// Arial's (and Arial Bold's) digit advance, over its unitsPerEm — measured
// 2026-09-25 from the Windows system files (plan.md §3.3). A cited constant,
// not derived: capsize has no per-glyph advance data.
const ARIAL_DIGIT_ADVANCE_OVER_UPM = 1139 / 2048;

const PLEX_SANS_WEIGHT_FILES: Record<400 | 500 | 600, string[]> = {
  400: ['IBMPlexSans-Regular-Latin1.woff2', 'IBMPlexSans-Regular-Latin2.woff2', 'IBMPlexSans-Regular-Pi.woff2'],
  500: ['IBMPlexSans-Medium-Latin1.woff2', 'IBMPlexSans-Medium-Latin2.woff2', 'IBMPlexSans-Medium-Pi.woff2'],
  600: ['IBMPlexSans-SemiBold-Latin1.woff2', 'IBMPlexSans-SemiBold-Latin2.woff2', 'IBMPlexSans-SemiBold-Pi.woff2'],
};

interface FontFaceRule {
  family: string;
  weight: string;
  unicodeRange: string | undefined;
  srcValue: string;
  urls: string[];
  declarationOrder: number;
}

function requireFile(filePath: string, whatFor: string): Buffer {
  if (!existsSync(filePath)) {
    throw new Error(`expected ${filePath} (${whatFor}) — it does not exist yet.`);
  }
  return readFileSync(filePath);
}

function sha256Hex(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

function parseFontFaceRules(css: string): FontFaceRule[] {
  const root = postcss.parse(css);
  const rules: FontFaceRule[] = [];
  let order = 0;

  root.walkAtRules('font-face', (atRule) => {
    let family = '';
    let weight = '';
    let unicodeRange: string | undefined;
    let srcValue = '';

    atRule.walkDecls((decl) => {
      const value = decl.value.trim();
      if (decl.prop === 'font-family') {
        family = value.replace(/^['"]|['"]$/g, '');
      } else if (decl.prop === 'font-weight') {
        weight = value;
      } else if (decl.prop === 'unicode-range') {
        unicodeRange = value;
      } else if (decl.prop === 'src') {
        srcValue = value;
      }
    });

    const urls = [...srcValue.matchAll(/url\((['"]?)([^'")]*)\1\)/g)].map((match) => match[2] ?? '');
    rules.push({ family, weight, unicodeRange, srcValue, urls, declarationOrder: order });
    order += 1;
  });

  return rules;
}

/** `U+0000-00FF`, `U+0131`, `U+00A0-00A9,U+00AB-00AC` style ranges, `?` wildcards included. */
function unicodeRangeCodePoints(spec: string): number[] {
  const codePoints: number[] = [];

  for (const rawToken of spec.split(',')) {
    const token = rawToken.trim().replace(/^U\+/i, '');
    if (token.length === 0) {
      continue;
    }

    if (token.includes('-')) {
      const [start, end] = token.split('-');
      for (let codePoint = parseInt(start!, 16); codePoint <= parseInt(end!, 16); codePoint++) {
        codePoints.push(codePoint);
      }
    } else if (token.includes('?')) {
      const start = parseInt(token.replace(/\?/g, '0'), 16);
      const end = parseInt(token.replace(/\?/g, 'F'), 16);
      for (let codePoint = start; codePoint <= end; codePoint++) {
        codePoints.push(codePoint);
      }
    } else {
      codePoints.push(parseInt(token, 16));
    }
  }

  return codePoints;
}

function percentToNumber(value: string): number {
  const match = value.trim().match(/^(-?[\d.]+)%$/);
  if (!match) {
    throw new Error(`'${value}' is not a percentage this test understands.`);
  }
  return Number(match[1]);
}

function round4(value: number): number {
  return Math.round(value * 10_000) / 10_000;
}

/** camelCase capsize descriptor keys, mapped to the kebab-case CSS property names fonts.css declares. */
const DESCRIPTOR_CSS_NAMES: Record<string, string> = {
  sizeAdjust: 'size-adjust',
  ascentOverride: 'ascent-override',
  descentOverride: 'descent-override',
  lineGapOverride: 'line-gap-override',
};

function assertDescriptorsMatch(
  declared: Map<string, string>,
  expected: Record<string, string | undefined>,
  faceLabel: string,
): void {
  // Only the four override properties are compared — capsize's descriptor
  // object also carries `fontFamily` and `src`, neither of which is a
  // size-adjust/ascent/descent/line-gap override, and neither belongs in
  // this "same set of descriptors" check.
  const relevantEntries = Object.entries(expected).filter(([key]) => key in DESCRIPTOR_CSS_NAMES);

  const expectedKeys = relevantEntries
    .filter(([, value]) => value !== undefined)
    .map(([key]) => DESCRIPTOR_CSS_NAMES[key]!);
  const declaredKeys = [...declared.keys()].filter((key) => Object.values(DESCRIPTOR_CSS_NAMES).includes(key));

  expect(new Set(declaredKeys), `${faceLabel}: declared descriptor set differs from capsize's`).toEqual(
    new Set(expectedKeys),
  );

  for (const [camelKey, expectedValue] of relevantEntries) {
    if (expectedValue === undefined) {
      continue;
    }
    const cssName = DESCRIPTOR_CSS_NAMES[camelKey]!;
    const declaredValue = declared.get(cssName);
    expect(declaredValue, `${faceLabel} has no ${cssName} declared`).toBeDefined();
    expect(round4(percentToNumber(declaredValue!)), `${faceLabel}'s ${cssName}`).toBe(
      round4(percentToNumber(expectedValue)),
    );
  }
}

function declarationsOf(rule: FontFaceRule, css: string): Map<string, string> {
  // Re-walk just this rule's declarations, keyed by property name (rules are
  // parsed once per call site; cheap for a handful of font-face blocks).
  const root = postcss.parse(css);
  const declarations = new Map<string, string>();
  let index = 0;

  root.walkAtRules('font-face', (atRule) => {
    if (index === rule.declarationOrder) {
      atRule.walkDecls((decl) => {
        declarations.set(decl.prop, decl.value.trim());
      });
    }
    index += 1;
  });

  return declarations;
}

describe('fonts.css (spec 261 US1/US2, issue #2333)', () => {
  it('each committed woff2 has IBM’s own SHA-256, and OFL.txt matches after CRLF→LF (S1)', () => {
    const problems: string[] = [];

    for (const [fileName, expectedSha] of Object.entries(SHA256_BY_FILE)) {
      const filePath = path.join(fontsDir, fileName);
      if (!existsSync(filePath)) {
        problems.push(`${fileName} does not exist at ${filePath}.`);
        continue;
      }
      const actualSha = sha256Hex(readFileSync(filePath));
      if (actualSha !== expectedSha) {
        problems.push(`${fileName}: expected sha256 ${expectedSha}, got ${actualSha}.`);
      }
    }

    const oflPath = path.join(fontsDir, 'OFL.txt');
    if (!existsSync(oflPath)) {
      problems.push(`OFL.txt does not exist at ${oflPath}.`);
    } else {
      const normalized = readFileSync(oflPath).toString('latin1').replace(/\r\n/g, '\n');
      const actualSha = createHash('sha256').update(Buffer.from(normalized, 'latin1')).digest('hex');
      if (actualSha !== OFL_SHA256) {
        problems.push(`OFL.txt: expected sha256 ${OFL_SHA256} after CRLF→LF, got ${actualSha}.`);
      }
    }

    expect(problems, problems.join('\n')).toEqual([]);
  });

  it('every code point a Plex @font-face declares is present in the cmap of the file its url() names (S2)', () => {
    const css = requireFile(fontsCssPath, 'the twelve Plex @font-face rules').toString('utf8');
    const rules = parseFontFaceRules(css).filter((rule) => rule.family.startsWith('IBM Plex'));

    expect(rules.length, 'no IBM Plex @font-face rule was found in fonts.css — the scan is broken.').toBeGreaterThan(0);

    const problems: string[] = [];

    for (const rule of rules) {
      if (rule.urls.length === 0) {
        // A fallback face (`local()` only, no `url()`) has no cmap to check.
        continue;
      }

      if (rule.unicodeRange === undefined) {
        // No unicode-range implicitly claims to cover every code point —
        // that is exactly the case this fact exists to catch, not skip.
        problems.push(`${rule.family} ${rule.weight} (${rule.urls[0]}): has no unicode-range.`);
        continue;
      }

      const url = rule.urls[0]!;
      const filePath = path.join(fontsDir, url);
      if (!existsSync(filePath)) {
        problems.push(`${rule.family} ${rule.weight}: url() names ${url}, which does not exist.`);
        continue;
      }

      const font = parseWoff2(readFileSync(filePath));
      const missing = unicodeRangeCodePoints(rule.unicodeRange).filter(
        (codePoint) => !font.cmap.codePoints.has(codePoint),
      );

      if (missing.length > 0) {
        problems.push(
          `${rule.family} ${rule.weight} (${url}) declares ${missing.length} code point(s) its cmap cannot draw, ` +
            `e.g. U+${missing[0]!.toString(16).toUpperCase()}.`,
        );
      }
    }

    expect(problems, problems.join('\n')).toEqual([]);
  });

  it(
    'every non-ASCII character the product renders is inside the unicode-range of some IBM Plex Sans ' +
      'weight-400 face (S3)',
    () => {
      const found = collectNonAsciiCharacters();

      expect(
        found.size,
        'no non-ASCII character was found anywhere under apps/{kiosk-web,management-web,shared}/src — ' +
          'the scan is broken, not the code.',
      ).toBeGreaterThanOrEqual(1);

      const css = requireFile(fontsCssPath, 'the Plex Sans weight-400 faces').toString('utf8');
      const weight400SansRanges = parseFontFaceRules(css)
        .filter((rule) => rule.family === 'IBM Plex Sans' && rule.weight === '400' && rule.unicodeRange !== undefined)
        .map((rule) => rule.unicodeRange!);

      expect(
        weight400SansRanges.length,
        'fonts.css declares no IBM Plex Sans weight-400 @font-face with a unicode-range.',
      ).toBeGreaterThan(0);

      const uncovered = [...found.entries()].filter(
        ([codePoint]) => !weight400SansRanges.some((range) => unicodeRangeCodePoints(range).includes(codePoint)),
      );

      expect(
        uncovered.map(([codePoint, example]) => `U+${codePoint.toString(16).toUpperCase()} (e.g. ${example})`),
      ).toEqual([]);
    },
  );

  it('IBM Plex Sans Fallback 400 equals createFontStack’s output for (Plex, Arial) (S4)', () => {
    const css = requireFile(fontsCssPath, 'the IBM Plex Sans Fallback 400 face').toString('utf8');
    const rule = parseFontFaceRules(css).find(
      (candidate) =>
        candidate.family === 'IBM Plex Sans Fallback' &&
        candidate.weight === '400' &&
        candidate.unicodeRange === undefined,
    );

    expect(rule, 'no general IBM Plex Sans Fallback 400 face (without unicode-range) in fonts.css').toBeDefined();

    const capsize = createFontStack([iBMPlexSans400, arial], { fontFaceFormat: 'styleObject' });
    const descriptor = capsize.fontFaces[0]!['@font-face'] as Record<string, string | undefined>;

    assertDescriptorsMatch(declarationsOf(rule!, css), descriptor, 'IBM Plex Sans Fallback 400');
  });

  // Fact E4 (fonts.css's own "WHY THE SANS 500/600..." comment, issue #2333's
  // CI failure): Sans 500/600's general fallback face does NOT equal
  // createFontStack's continuous output. Headless Chromium (the e2e suite's
  // browser) hints every glyph's advance to a whole device pixel at 16px, and
  // capsize's mathematically-correct size-adjust can still round to the pixel
  // bucket next to Plex's own rather than Plex's own bucket. These two faces'
  // values were instead chosen empirically against real rendering in the
  // exact Chromium build the e2e suite runs (self-hosted-fonts.spec.ts E4,
  // measured 2026-09-26 via mcr.microsoft.com/playwright:v1.62.1-jammy) and
  // are asserted here as fixed constants, tied to Plex's own ascent/descent
  // via the same formula capsize itself uses — so a size-adjust edited
  // without recomputing the other three still fails this test.
  const HINTING_SAFE_GENERAL_SIZE_ADJUST_PERCENT: Record<500 | 600, number> = { 500: 100, 600: 100 };

  it.each([
    [500, iBMPlexSans500],
    [600, iBMPlexSans600],
  ] as const)(
    'IBM Plex Sans Fallback %s uses the hinting-safe measured size-adjust, tied to Plex’s own ascent/descent (S4, E4)',
    (weight, plexMetrics) => {
      const css = requireFile(fontsCssPath, `the IBM Plex Sans Fallback ${weight} face`).toString('utf8');
      const rule = parseFontFaceRules(css).find(
        (candidate) =>
          candidate.family === 'IBM Plex Sans Fallback' &&
          candidate.weight === String(weight) &&
          candidate.unicodeRange === undefined,
      );

      expect(
        rule,
        `no general IBM Plex Sans Fallback ${weight} face (without unicode-range) in fonts.css`,
      ).toBeDefined();

      const sizeAdjustFraction = HINTING_SAFE_GENERAL_SIZE_ADJUST_PERCENT[weight] / 100;
      const expected = {
        sizeAdjust: `${HINTING_SAFE_GENERAL_SIZE_ADJUST_PERCENT[weight]}%`,
        ascentOverride: `${(plexMetrics.ascent / plexMetrics.unitsPerEm / sizeAdjustFraction) * 100}%`,
        descentOverride: `${(Math.abs(plexMetrics.descent) / plexMetrics.unitsPerEm / sizeAdjustFraction) * 100}%`,
        lineGapOverride: `${(plexMetrics.lineGap / plexMetrics.unitsPerEm / sizeAdjustFraction) * 100}%`,
      };

      assertDescriptorsMatch(declarationsOf(rule!, css), expected, `IBM Plex Sans Fallback ${weight}`);

      if (weight === 600) {
        // The bold system font's own hinted advance widths have nothing
        // within 3% of Plex SemiBold's rendered prose width on the e2e
        // suite's Chromium (E4) — only the regular-weight local font's
        // buckets do. Guards against silently reverting to the bold source,
        // which would reintroduce the CI failure this fact exists to catch.
        expect(
          rule!.srcValue,
          'weight 600’s general fallback face must use the regular-weight local system font, not bold (E4)',
        ).not.toMatch(/Bold/i);
      }
    },
  );

  it('IBM Plex Mono Fallback 400 equals createFontStack’s output for (Plex Mono, Courier New) — and has no line-gap-override (S4)', () => {
    const css = requireFile(fontsCssPath, 'the IBM Plex Mono Fallback 400 face').toString('utf8');
    const rule = parseFontFaceRules(css).find(
      (candidate) =>
        candidate.family === 'IBM Plex Mono Fallback' &&
        candidate.weight === '400' &&
        candidate.unicodeRange === undefined,
    );

    expect(rule, 'no general IBM Plex Mono Fallback 400 face (without unicode-range) in fonts.css').toBeDefined();

    const capsize = createFontStack([iBMPlexMono400, courierNew], { fontFaceFormat: 'styleObject' });
    const descriptor = capsize.fontFaces[0]!['@font-face'] as Record<string, string | undefined>;

    // Courier New's lineGap is 0, so capsize omits lineGapOverride entirely
    // (plan.md §3.2) — the declared face must match that omission exactly.
    expect(descriptor['lineGapOverride'], 'capsize is expected to omit lineGapOverride for this pair').toBeUndefined();

    assertDescriptorsMatch(declarationsOf(rule!, css), descriptor, 'IBM Plex Mono Fallback 400');
  });

  // Fact E4 (fonts.css's own "WHY THE SANS 500/600..." comment, issue #2333's
  // CI failure): the continuous ratio below (real shipped-file digit advance
  // over ARIAL_DIGIT_ADVANCE_OVER_UPM) is right to several decimal places,
  // but headless Chromium hints the digit glyph's advance to a whole device
  // pixel at 16px, and that continuous value rounds to the pixel bucket next
  // to Plex's own. DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT is the value at the
  // centre of the empirically-verified bucket that does match (measured
  // 2026-09-26 via mcr.microsoft.com/playwright:v1.62.1-jammy, same for
  // every weight because the shipped digits share one advance across
  // weights — S4b's own first assertion below). The sanity bound after it
  // catches this constant drifting away from what a font update would derive
  // continuously, without demanding the impossible exact equality.
  const DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT = 112.5;

  it.each([400, 500, 600] as const)(
    'all ten digits in the shipped Sans %s Latin1 file share one advance, and a digit fallback face cites the hinting-safe size-adjust (S4b, E4)',
    (weight) => {
      const latin1File = PLEX_SANS_WEIGHT_FILES[weight][0]!;
      const filePath = path.join(fontsDir, latin1File);
      const font = parseWoff2(requireFile(filePath, `the Sans ${weight} Latin1 file`));

      const digitAdvances = [...'0123456789'].map((digit) => {
        const glyphId = font.cmap.glyphId(digit.codePointAt(0)!);
        expect(glyphId, `${latin1File}'s cmap has no glyph for '${digit}'`).toBeDefined();
        return font.hmtx.advanceWidth(glyphId!);
      });

      expect(new Set(digitAdvances).size, `${latin1File}: not all ten digits share one advance`).toBe(1);
      const digitAdvance = digitAdvances[0]!;

      const analyticalSizeAdjustPercent = (digitAdvance / font.head.unitsPerEm / ARIAL_DIGIT_ADVANCE_OVER_UPM) * 100;
      expect(
        Math.abs(DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT - analyticalSizeAdjustPercent),
        `${latin1File}: the hinting-safe digit size-adjust (${DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT}%) has drifted ` +
          `too far from the continuous value this file's own metrics derive (${analyticalSizeAdjustPercent}%) — ` +
          're-measure E4 against real rendering before trusting the constant',
      ).toBeLessThanOrEqual(6);

      const sizeAdjustFraction = DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT / 100;
      const expected = {
        sizeAdjust: `${DIGIT_HINTING_SAFE_SIZE_ADJUST_PERCENT}%`,
        ascentOverride: `${(font.hhea.ascender / font.head.unitsPerEm / sizeAdjustFraction) * 100}%`,
        descentOverride: `${(Math.abs(font.hhea.descender) / font.head.unitsPerEm / sizeAdjustFraction) * 100}%`,
        lineGapOverride: `${(font.hhea.lineGap / font.head.unitsPerEm / sizeAdjustFraction) * 100}%`,
      };

      const css = requireFile(fontsCssPath, `the Sans ${weight} digit fallback face`).toString('utf8');
      const rules = parseFontFaceRules(css);
      const generalRule = rules.find(
        (candidate) =>
          candidate.family === 'IBM Plex Sans Fallback' &&
          candidate.weight === String(weight) &&
          candidate.unicodeRange === undefined,
      );
      const digitRule = rules.find(
        (candidate) =>
          candidate.family === 'IBM Plex Sans Fallback' &&
          candidate.weight === String(weight) &&
          candidate.unicodeRange === 'U+0030-0039',
      );

      expect(
        generalRule,
        `no general IBM Plex Sans Fallback ${weight} face to declare the digit face after`,
      ).toBeDefined();
      expect(digitRule, `no IBM Plex Sans Fallback ${weight} digit face (unicode-range: U+0030-0039)`).toBeDefined();
      expect(
        digitRule!.declarationOrder,
        `the digit fallback face for weight ${weight} must be declared AFTER its general fallback face`,
      ).toBeGreaterThan(generalRule!.declarationOrder);

      assertDescriptorsMatch(declarationsOf(digitRule!, css), expected, `IBM Plex Sans Fallback ${weight} digits`);
    },
  );

  it.each([
    ['IBMPlexSans-Regular-Latin1.woff2', iBMPlexSans400],
    ['IBMPlexSans-Regular-Latin2.woff2', iBMPlexSans400],
    ['IBMPlexSans-Regular-Pi.woff2', iBMPlexSans400],
    ['IBMPlexSans-Medium-Latin1.woff2', iBMPlexSans500],
    ['IBMPlexSans-Medium-Latin2.woff2', iBMPlexSans500],
    ['IBMPlexSans-Medium-Pi.woff2', iBMPlexSans500],
    ['IBMPlexSans-SemiBold-Latin1.woff2', iBMPlexSans600],
    ['IBMPlexSans-SemiBold-Latin2.woff2', iBMPlexSans600],
    ['IBMPlexSans-SemiBold-Pi.woff2', iBMPlexSans600],
    ['IBMPlexMono-Regular-Latin1.woff2', iBMPlexMono400],
    ['IBMPlexMono-Regular-Latin2.woff2', iBMPlexMono400],
    ['IBMPlexMono-Regular-Pi.woff2', iBMPlexMono400],
  ] as const)(
    '%s’s head/hhea metrics equal @capsizecss/metrics’ entry for that family and weight (S5)',
    (fileName, metrics) => {
      const filePath = path.join(fontsDir, fileName);
      const font = parseWoff2(requireFile(filePath, `${fileName}'s metrics`));

      expect(font.head.unitsPerEm).toBe(metrics.unitsPerEm);
      expect(font.hhea.ascender).toBe(metrics.ascent);
      expect(font.hhea.descender).toBe(metrics.descent);
      expect(font.hhea.lineGap).toBe(metrics.lineGap);
    },
  );
});

/**
 * Every non-ASCII character in a string literal, template literal or JSX text
 * of a non-test `.ts`/`.tsx` file under `apps/{kiosk-web,management-web,
 * shared}/src` — via the TypeScript compiler API, so a comment cannot count
 * (spec 261 plan.md §6.2 S3). Returns each distinct code point with one
 * example occurrence, for a readable failure message.
 */
function collectNonAsciiCharacters(): Map<number, string> {
  const appsRoot = path.resolve(fontsDir, '..', '..', '..', '..');
  const trees = ['kiosk-web', 'management-web', 'shared'].map((app) => path.join(appsRoot, app, 'src'));

  const found = new Map<number, string>();

  for (const tree of trees) {
    for (const filePath of listSourceFiles(tree)) {
      const sourceText = readFileSync(filePath, 'utf8');
      const sourceFile = ts.createSourceFile(
        filePath,
        sourceText,
        ts.ScriptTarget.Latest,
        true,
        filePath.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
      );

      visitForLiterals(sourceFile, (text) => {
        for (const character of text) {
          const codePoint = character.codePointAt(0)!;
          if (codePoint > 0x7f && !found.has(codePoint)) {
            found.set(codePoint, `${path.relative(appsRoot, filePath)}: "${character}"`);
          }
        }
      });
    }
  }

  return found;
}

function visitForLiterals(node: ts.Node, onText: (text: string) => void): void {
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isJsxText(node)) {
    onText(node.text);
  } else if (ts.isTemplateExpression(node)) {
    onText(node.head.text);
    for (const span of node.templateSpans) {
      onText(span.literal.text);
    }
  }

  ts.forEachChild(node, (child) => visitForLiterals(child, onText));
}

function listSourceFiles(dir: string): string[] {
  if (!existsSync(dir)) {
    return [];
  }

  const files: string[] = [];
  const entries: Dirent[] = readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...listSourceFiles(fullPath));
    } else if (
      (entry.name.endsWith('.ts') || entry.name.endsWith('.tsx')) &&
      !entry.name.includes('.test.') &&
      !entry.name.endsWith('.d.ts')
    ) {
      files.push(fullPath);
    }
  }

  return files;
}
