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
// Red on develop (spec 256 §6): every assertion below except `.rounded-md`,
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

describe('management-web tokens compile through Tailwind (spec 256 US2)', () => {
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
});
