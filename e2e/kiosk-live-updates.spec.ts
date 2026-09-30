import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';

/**
 * Spec 297 (issue #2635) T010, plan.md §5.5. The only check on this leg that
 * watches the browser, not a resolver: paints a computed colour into a 1x1
 * canvas and reads the alpha/RGB back, the same technique the architect used
 * to reproduce the hue-drift premise (spec §1, T001/V1). Local to this file
 * (one caller, ADR-0162) — not lifted into `e2e/support/*`.
 */
async function readBackColour(page: Page, cssColour: string): Promise<{ r: number; g: number; b: number; a: number }> {
  return page.evaluate((colour) => {
    const canvas = document.createElement('canvas');
    canvas.width = 1;
    canvas.height = 1;
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = colour;
    ctx.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
    return { r: r!, g: g!, b: b!, a: a! };
  }, cssColour);
}

/** CSS Color 4 OKLab hue angle, degrees in [0, 360). */
function oklabHueDegrees(r: number, g: number, b: number): number {
  const toLinear = (c: number) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  const [lr, lg, lb] = [toLinear(r), toLinear(g), toLinear(b)];

  const l = 0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb;
  const m = 0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb;
  const s = 0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb;

  const [lp, mp, sp] = [Math.cbrt(l), Math.cbrt(m), Math.cbrt(s)];

  const a = 1.9779984951 * lp - 2.428592205 * mp + 0.4505937099 * sp;
  const bComponent = 0.0259040371 * lp + 0.7827717662 * mp - 0.808675766 * sp;

  const hueRadians = Math.atan2(bComponent, a);
  const hueDegrees = (hueRadians * 180) / Math.PI;
  return hueDegrees < 0 ? hueDegrees + 360 : hueDegrees;
}

function hueDifferenceDegrees(a: number, b: number): number {
  const diff = Math.abs(a - b) % 360;
  return diff > 180 ? 360 - diff : diff;
}

// Spec 011 US2 (FR-006/007) — the kiosk shows the discreet degraded badge
// while the layout hub is unreachable and clears it once the unbounded retry
// ladder reconnects. The outage is induced client-side by aborting every
// /hubs/** request (negotiate + transport), which the app must treat exactly
// like a network outage.
//
// Spec 041: the sign-in helper moved to e2e/support/kiosk-session.ts. The local
// copy accepted "could not load layouts" as a passing outcome, so this file
// went green for years against a kiosk that could never show a wall.

test('kiosk shows the degraded badge while the hub is unreachable and clears it after recovery', async ({ page }) => {
  await signInToKiosk(page);

  // Kill the hub and reload so even the INITIAL connect fails (FR-006:
  // initial-connect failures must retry indefinitely, never give up).
  await page.route('**/hubs/**', (route) => route.abort());
  await page.reload();
  const badge = page.getByTestId('live-updates-degraded');
  await expect(badge).toBeVisible();

  // FR-011: opaque, not translucent over video (today's `bg-accent-warning/15`
  // reads back alpha ~38, not 255).
  const computedBackground = await badge.evaluate((el) => getComputedStyle(el).backgroundColor);
  const { a: badgeAlpha } = await readBackColour(page, computedBackground);
  expect(badgeAlpha, `the degraded chip's rendered background alpha (from '${computedBackground}')`).toBe(255);

  // FR-012: one line of text-xs plus py-1, no taller than 24 CSS px.
  const box = await badge.boundingBox();
  expect(box, 'the degraded chip has a bounding box').not.toBeNull();
  expect(box!.height).toBeLessThanOrEqual(24);

  // FR-011: each triad tint's rendered hue is within 20 degrees of its own
  // triad's rendered hue — the browser-observed form of spec §1's finding
  // (today all three color-mix tints drift toward the ground's hue, slate).
  for (const role of ['active', 'warning', 'fault'] as const) {
    const [tintColour, triadColour] = await page.evaluate((r) => {
      const style = getComputedStyle(document.documentElement);
      return [
        style.getPropertyValue(`--color-accent-${r}-subtle`).trim(),
        style.getPropertyValue(`--color-accent-${r}`).trim(),
      ];
    }, role);

    const tint = await readBackColour(page, tintColour);
    const triad = await readBackColour(page, triadColour);
    const tintHue = oklabHueDegrees(tint.r, tint.g, tint.b);
    const triadHue = oklabHueDegrees(triad.r, triad.g, triad.b);
    const diff = hueDifferenceDegrees(tintHue, triadHue);

    expect(
      diff,
      `--color-accent-${role}-subtle (${tintColour} -> hue ${tintHue.toFixed(1)}) vs --color-accent-${role} ` +
        `(${triadColour} -> hue ${triadHue.toFixed(1)})`,
    ).toBeLessThanOrEqual(20);
  }

  // Restore the network. The retry ladder tops out at 30 s ±20 % jitter, so
  // the next attempt lands within ~36 s of recovery — allow 45 s.
  await page.unroute('**/hubs/**');
  await expect(page.getByTestId('live-updates-degraded')).toBeHidden({ timeout: 45_000 });
});
