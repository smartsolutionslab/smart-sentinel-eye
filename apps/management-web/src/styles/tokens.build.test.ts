// @vitest-environment node
//
// Compiles this app's REAL src/styles/index.css with the installed
// @tailwindcss/postcss (4.3.3) — not a mock, the actual compiler both the
// dev server and `vite build` use — through a probe sheet that imports it and
// names every candidate class this spec cares about via `@source inline(...)`
// (plan.md §5.3, measured fact F9). `base` is set to the app root so the
// config's relative `content` globs resolve exactly as they do under Vite
// (plan.md §10 risk R1).
//
// Red on develop (spec 257 §6): every assertion below except `.rounded-md`,
// which is a green pin — Tailwind 4.3.3 already emits
// `border-radius: var(--radius-md)` for the STOCK `rounded-md` utility, by a
// name collision with ADR-0148's token names (spec §1 finding 2), unrelated
// to anything this spec adds.
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import postcss from 'postcss';
import tailwindcss from '@tailwindcss/postcss';

const stylesDir = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(stylesDir, '..', '..');
const probePath = path.join(stylesDir, '__probe__.css');

// Every candidate this spec's US2 acceptance scenarios name (plan.md §5.3).
const CANDIDATES = [
  'p-4',
  'p-0.5',
  'text-sm',
  'text-2xl',
  'rounded-md',
  'shadow-popover',
  'shadow-overlay',
  'shadow-xl',
  'shadow-md',
  'rounded-3xl',
  'text-7xl',
  'font-bold',
  'duration-fast',
  'ease-out',
  'z-overlay',
  'bg-bg-raised',
  'bg-scrim',
  'bg-accent',
  'bg-accent-hover',
  'border-border-subtle',
  'text-fg-on-accent',
  'ring-focus-ring',
  // Spec 293 (issue #2342) tasks.md T006 — the font-size slider's
  // `accent-accent` (OverlayEditor.tsx), styling the native
  // <input type="range"> instead of a Slider primitive (spec §4.1).
  'accent-accent',
  // Spec 268 (issue #2336) plan.md §5.3 — the five roles US1/US2 add.
  'bg-bg-hover',
  'bg-bg-pressed',
  'bg-accent-fault-hover',
  'bg-accent-fault-pressed',
  'text-fg-on-fault',
  // Pins (plan.md §5.3): already true on develop, not phase-4a evidence.
  'hover:bg-accent-hover',
  'focus-visible:outline-focus-ring',
  'active:bg-accent-pressed',
  'disabled:bg-transparent',
  'aria-disabled:bg-transparent',
  // Spec 292 (issue #2334) T004, plan.md §6 — the motion roles and the four
  // surface/scrim animations. RED on develop: none of these exist yet.
  'duration-state',
  'duration-enter',
  'duration-exit',
  'duration-route',
  'ease-state',
  'ease-enter',
  'ease-exit',
  'ease-route',
  'animate-surface-enter',
  'animate-surface-exit',
  'animate-scrim-enter',
  'animate-scrim-exit',
  // Spec 292, plan.md §6 — must stop compiling once the `animation` namespace
  // replaces Tailwind's stock one (plan.md §3, "not under extend").
  'animate-spin',
  'animate-ping',
  'animate-pulse',
  'animate-bounce',
];

let root: postcss.Root;
let rawOutput: string;

beforeAll(async () => {
  const probeSource = `@import './index.css';\n@source inline("${CANDIDATES.join(' ')}");\n`;
  writeFileSync(probePath, probeSource, 'utf8');

  const result = await postcss([tailwindcss({ base: appRoot })]).process(probeSource, {
    from: probePath,
  });

  rawOutput = result.css;
  root = postcss.parse(rawOutput);
}, 30_000);

afterAll(() => {
  if (existsSync(probePath)) {
    rmSync(probePath, { force: true });
  }
});

/** The declarations of the one rule whose selector is exactly `.<className>`, normalising CSS escapes. */
function ruleDeclarations(className: string): Record<string, string> {
  const target = `.${className}`;
  const declarations: Record<string, string> = {};

  root.walkRules((rule) => {
    const normalizedSelector = rule.selector.replace(/\\([^\\])/g, '$1');
    if (normalizedSelector !== target) {
      return;
    }

    rule.walkDecls((decl) => {
      declarations[decl.prop] = decl.value;
    });
  });

  return declarations;
}

function ruleExists(className: string): boolean {
  const target = `.${className}`;
  let found = false;
  root.walkRules((rule) => {
    if (rule.selector.replace(/\\([^\\])/g, '$1') === target) {
      found = true;
    }
  });
  return found;
}

describe('management-web tokens compile through Tailwind (spec 257 US2)', () => {
  it('spacing routes through the rhythm tokens: p-4 padding is var(--space-4)', () => {
    expect(ruleExists('p-4'), '.p-4 does not compile at all').toBe(true);
    expect(ruleDeclarations('p-4').padding).toBe('var(--space-4)');
  });

  it('the one sub-rhythm step compiles too: p-0.5 padding is var(--space-0-5)', () => {
    expect(ruleExists('p-0.5'), '.p-0\\.5 does not compile at all — --space-0-5 is not on the spacing scale yet').toBe(
      true,
    );
    expect(ruleDeclarations('p-0.5').padding).toBe('var(--space-0-5)');
  });

  it('type carries its own leading: text-sm font-size is var(--text-sm), line-height falls back to var(--text-sm-leading)', () => {
    expect(ruleExists('text-sm'), '.text-sm does not compile at all').toBe(true);
    const declarations = ruleDeclarations('text-sm');
    expect(declarations['font-size']).toBe('var(--text-sm)');
    expect(declarations['line-height'] ?? '(no line-height declared)').toContain('var(--text-sm-leading)');
  });

  it('2xl carries tracking: text-2xl letter-spacing falls back to var(--tracking-tight)', () => {
    expect(ruleExists('text-2xl'), '.text-2xl does not compile at all').toBe(true);
    // Tailwind 4.3.3 always wraps a fontSize tuple's letterSpacing as
    // `var(--tw-tracking, var(--tracking-tight))`, never a bare value — the
    // same wrapped-default shape as text-sm's line-height above.
    expect(ruleDeclarations('text-2xl')['letter-spacing'] ?? '(no letter-spacing declared)').toContain(
      'var(--tracking-tight)',
    );
  });

  it("rounded-md compiles to var(--radius-md) (green pin — Tailwind 4.3.3's own name collision, not this spec's doing)", () => {
    expect(ruleExists('rounded-md')).toBe(true);
    expect(ruleDeclarations('rounded-md')['border-radius']).toBe('var(--radius-md)');
  });

  it.each([
    // Tailwind 4.3.3's shadow-* utility always emits box-shadow as the fixed
    // 5-variable composition (…, var(--tw-shadow)) and sets the token
    // reference on --tw-shadow itself — the same indirection as ring-*
    // below, whose token lands on --tw-ring-color rather than box-shadow.
    ['shadow-popover', '--tw-shadow', '--shadow-popover'],
    ['shadow-overlay', '--tw-shadow', '--shadow-overlay'],
    ['duration-fast', 'transition-duration', '--duration-fast'],
    ['z-overlay', 'z-index', '--z-overlay'],
    ['bg-bg-raised', 'background-color', '--color-bg-raised'],
    ['bg-scrim', 'background-color', '--color-scrim'],
    ['bg-accent', 'background-color', '--color-accent'],
    ['bg-accent-hover', 'background-color', '--color-accent-hover'],
    ['border-border-subtle', 'border-color', '--color-border-subtle'],
    ['text-fg-on-accent', 'color', '--color-fg-on-accent'],
    ['ring-focus-ring', '--tw-ring-color', '--color-focus-ring'],
    // Spec 268 (issue #2336) plan.md §5.3 — RED on develop: none of the five
    // roles below exist in tokens.css yet.
    ['bg-bg-hover', 'background-color', '--color-bg-hover'],
    ['bg-bg-pressed', 'background-color', '--color-bg-pressed'],
    ['bg-accent-fault-hover', 'background-color', '--color-accent-fault-hover'],
    ['bg-accent-fault-pressed', 'background-color', '--color-accent-fault-pressed'],
    ['text-fg-on-fault', 'color', '--color-fg-on-fault'],
    // Spec 293 (issue #2342) tasks.md T006.
    ['accent-accent', 'accent-color', '--color-accent'],
  ])('role-named utility %s cites its matching token', (className, property, expectedToken) => {
    expect(ruleExists(className), `.${className} does not compile at all yet`).toBe(true);
    const declarations = ruleDeclarations(className);
    expect(declarations[property] ?? '(not declared)').toContain(expectedToken);
  });

  it(
    'role-named utility ease-out cites its matching token — GREEN ON DEVELOP TOO, undeclared pin: Tailwind ' +
      '4.3.3 ships its own --ease-out theme variable (node_modules/tailwindcss/theme.css), the same name ' +
      'collision spec.md §1 finding 2 names for --text-sm/--font-sans/--tracking-wide but which spec §6 and ' +
      'plan.md §5.3 pin only for rounded-md. Not phase-4a evidence for this one assertion — see the phase-4a ' +
      'report.',
    () => {
      expect(ruleExists('ease-out')).toBe(true);
      expect(ruleDeclarations('ease-out')['transition-timing-function']).toContain('--ease-out');
    },
  );

  // Spec 268 (issue #2336) plan.md §5.3 — three PINS, already true on
  // develop (Tailwind 4.3.3's own behaviour), not phase-4a evidence.
  it('hover:bg-accent-hover compiles inside @media (hover: hover) — GREEN PIN (Tailwind 4.3.3 touch guard)', () => {
    // A hover: variant's selector carries the pseudo-class too
    // (`.hover\:bg-accent-hover:hover`), so this cannot reuse `ruleExists` —
    // that helper's exact `.${className}` match is for prefixless utilities.
    let found = false;
    let sitsInsideHoverMedia = false;

    root.walkAtRules('media', (atRule) => {
      if (atRule.params !== '(hover: hover)') {
        return;
      }
      atRule.walkRules((rule) => {
        if (rule.selector.replace(/\\([^\\])/g, '$1') === '.hover:bg-accent-hover:hover') {
          found = true;
          sitsInsideHoverMedia = true;
        }
      });
    });

    expect(found, '.hover\\:bg-accent-hover:hover does not compile at all').toBe(true);
    expect(sitsInsideHoverMedia, 'expected .hover\\:bg-accent-hover:hover inside @media (hover: hover)').toBe(true);
  });

  it('focus-visible:outline-focus-ring compiles to outline-color: var(--color-focus-ring) — GREEN PIN (the token already exists)', () => {
    let found = false;
    const declarations: Record<string, string> = {};

    root.walkRules((rule) => {
      if (rule.selector.replace(/\\([^\\])/g, '$1') === '.focus-visible:outline-focus-ring:focus-visible') {
        found = true;
        rule.walkDecls((decl) => {
          declarations[decl.prop] = decl.value;
        });
      }
    });

    expect(found, '.focus-visible\\:outline-focus-ring:focus-visible does not compile at all').toBe(true);
    expect(declarations['outline-color']).toBe('var(--color-focus-ring)');
  });

  it(
    'hover, active, disabled and aria-disabled utilities appear in that order in the compiled output — GREEN ' +
      'PIN (plan.md §3 R1: the state matrix depends on this cascade order, and Button.tsx §5.1 pins it here so ' +
      'a Tailwind upgrade that reorders it fails loudly)',
    () => {
      const hoverIndex = rawOutput.indexOf('.hover\\:bg-accent-hover');
      const activeIndex = rawOutput.indexOf('.active\\:bg-accent-pressed');
      const disabledIndex = rawOutput.indexOf('.disabled\\:bg-transparent');
      const ariaDisabledIndex = rawOutput.indexOf('.aria-disabled\\:bg-transparent');

      expect(hoverIndex).toBeGreaterThanOrEqual(0);
      expect(activeIndex).toBeGreaterThanOrEqual(0);
      expect(disabledIndex).toBeGreaterThanOrEqual(0);
      expect(ariaDisabledIndex).toBeGreaterThanOrEqual(0);

      expect(hoverIndex).toBeLessThan(activeIndex);
      expect(activeIndex).toBeLessThan(disabledIndex);
      expect(disabledIndex).toBeLessThan(ariaDisabledIndex);
    },
  );

  it.each(['shadow-xl', 'shadow-md', 'rounded-3xl', 'text-7xl', 'font-bold'])(
    'closed scales reject the stock value %s — no rule is emitted',
    (className) => {
      expect(ruleExists(className)).toBe(false);
    },
  );

  it("Tailwind's transition defaults are bound to the motion tokens", () => {
    expect(rawOutput).toContain('--default-transition-duration: var(--duration-fast)');
    expect(rawOutput).toContain('--default-transition-timing-function: var(--ease-out)');
  });

  // Spec 292 (issue #2334) T004, plan.md §6 — RED on develop: none of the
  // eight role tokens exist yet, so neither the role utilities nor the
  // animate-* utilities that cite them compile, and the stock animate-*
  // namespace has not yet been replaced. management-web is also where the
  // reduced-motion redefinition and the route cross-fade timing are proved —
  // it is the only app that imports motion.css (spec 292 §3, plan.md §1).
  describe('motion roles and surface/scrim animations (spec 292)', () => {
    it.each([
      ['duration-state', 'transition-duration', '--duration-state'],
      ['duration-enter', 'transition-duration', '--duration-enter'],
      ['duration-exit', 'transition-duration', '--duration-exit'],
      ['duration-route', 'transition-duration', '--duration-route'],
      ['ease-state', 'transition-timing-function', '--ease-state'],
      ['ease-enter', 'transition-timing-function', '--ease-enter'],
      ['ease-exit', 'transition-timing-function', '--ease-exit'],
      ['ease-route', 'transition-timing-function', '--ease-route'],
    ])('role-named utility %s cites its matching token', (className, property, expectedToken) => {
      expect(ruleExists(className), `.${className} does not compile at all yet`).toBe(true);
      const declarations = ruleDeclarations(className);
      expect(declarations[property] ?? '(not declared)').toContain(expectedToken);
    });

    it.each([
      ['animate-surface-enter', 'sse-surface-enter', '--duration-enter', '--ease-enter'],
      ['animate-surface-exit', 'sse-surface-exit', '--duration-exit', '--ease-exit'],
      ['animate-scrim-enter', 'sse-scrim-enter', '--duration-enter', '--ease-enter'],
      ['animate-scrim-exit', 'sse-scrim-exit', '--duration-exit', '--ease-exit'],
    ])(
      'animation utility %s cites its keyframe name and role tokens',
      (className, keyframeName, durationToken, easeToken) => {
        expect(ruleExists(className), `.${className} does not compile at all yet`).toBe(true);
        const animationValue = ruleDeclarations(className).animation ?? '(not declared)';
        expect(animationValue).toContain(keyframeName);
        expect(animationValue).toContain(durationToken);
        expect(animationValue).toContain(easeToken);
      },
    );

    it.each(['animate-spin', 'animate-ping', 'animate-pulse', 'animate-bounce'])(
      "the stock animation utility %s no longer compiles — the shared theme's `animation` namespace replaces it, not `extend`s it",
      (className) => {
        expect(ruleExists(className)).toBe(false);
      },
    );

    it('the surface-enter keyframe compiles both outside and inside prefers-reduced-motion, the inner one opacity-only', () => {
      let outsideFound = false;
      let insideReduceOpacityOnly = false;

      root.walkAtRules('keyframes', (atRule) => {
        if (atRule.params !== 'sse-surface-enter') return;

        const insideReduceMedia =
          atRule.parent?.type === 'atrule' &&
          (atRule.parent as postcss.AtRule).name === 'media' &&
          (atRule.parent as postcss.AtRule).params === '(prefers-reduced-motion: reduce)';

        if (!insideReduceMedia) {
          outsideFound = true;
          return;
        }

        let onlyOpacity = true;
        atRule.walkDecls((decl) => {
          if (decl.prop !== 'opacity') {
            onlyOpacity = false;
          }
        });
        insideReduceOpacityOnly = onlyOpacity;
      });

      expect(outsideFound, '@keyframes sse-surface-enter not found outside any @media').toBe(true);
      expect(
        insideReduceOpacityOnly,
        '@keyframes sse-surface-enter not found (opacity-only) inside prefers-reduced-motion',
      ).toBe(true);
    });

    it('the route cross-fade cites --duration-route and --ease-route on the view-transition pseudo-elements', () => {
      expect(rawOutput).toContain('::view-transition-old(root)');
      expect(rawOutput).toContain('::view-transition-new(root)');
      expect(rawOutput).toContain('animation-duration: var(--duration-route)');
      expect(rawOutput).toContain('animation-timing-function: var(--ease-route)');
    });
  });
});
