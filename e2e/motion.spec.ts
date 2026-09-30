import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { clickSidebarLink } from './support/management-navigation';

// Spec 292 (issue #2334), tasks.md T002/T005, plan.md §7 — motion roles read from a
// real browser (ADR-0146/0148). jsdom computes neither CSS custom properties nor
// Web Animations timings, so every assertion below except the one T002
// characterisation is only observable here.
//
// Phase-4a colour: item 5 (Button timing) is CHARACTERISATION (T002) — observed
// green on `develop` before Button.tsx migrates to the `duration-state`/`ease-state`
// role tokens, and must stay green, unmodified, afterwards (T011). Every other test
// in this file is RED on unmodified `develop` (T005): no `sse-surface-*`/
// `sse-scrim-*` animation exists yet, and no route cross-fade is wired.
//
// Waits are by condition throughout (ADR-0150). Capturing an animation that runs for
// 160-200ms races a poll against the animation's own end, so `installMotionRecorder`
// hooks `animationstart` via `page.addInitScript` before any navigation and every
// case reads its recording instead of racing `document.getAnimations()` after the
// fact (plan.md §7).

interface RecordedAnimation {
  animationName: string;
  pseudoElement: string | null;
  durationMs: number | null;
  easing: string | null;
  properties: string[];
}

/**
 * `lib.dom.d.ts`'s `GetAnimationsOptions` has no `pseudoElement` field, even
 * though every evergreen browser's `Element.getAnimations()` accepts one (the
 * only way to read back a pseudo-element's own animations, which the route
 * cross-fade case needs for `::view-transition-old/new(root)`).
 */
interface ElementWithPseudoElementAnimations {
  getAnimations(options?: { subtree?: boolean; pseudoElement?: string }): Animation[];
}

/**
 * Installs a recorder that survives every navigation in this page's lifetime
 * (`addInitScript` re-runs before each new document), so it must be called once,
 * before `signInAsOperator`'s first `page.goto`, not per-scenario.
 */
async function installMotionRecorder(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const events: RecordedAnimation[] = [];
    (window as unknown as { __motionEvents: RecordedAnimation[] }).__motionEvents = events;

    document.addEventListener(
      'animationstart',
      (event) => {
        const animationEvent = event as AnimationEvent & { pseudoElement?: string };
        const target = animationEvent.target;
        const element = target instanceof Document ? target.documentElement : target instanceof Element ? target : null;
        if (element === null) {
          return;
        }

        const pseudoElement = animationEvent.pseudoElement;
        // `subtree: true` is required, not merely harmless: without it,
        // `getAnimations({ pseudoElement })` returns nothing for
        // `::view-transition-old/new(root)` — confirmed empirically against a
        // live stack (frontend-engineer, spec 292 T016). The view-transition
        // pseudo-element tree is not a direct child of `documentElement` the
        // way `pseudoElement` alone assumes.
        const animations = (element as unknown as ElementWithPseudoElementAnimations).getAnimations({
          subtree: true,
          pseudoElement,
        }) as CSSAnimation[];
        const match = animations.find((animation) => animation.animationName === animationEvent.animationName);
        if (match === undefined || !(match.effect instanceof KeyframeEffect)) {
          return;
        }

        const timing = match.effect.getComputedTiming();
        const keyframes = match.effect.getKeyframes();
        const keyframeProperties = new Set<string>();
        for (const frame of keyframes) {
          for (const key of Object.keys(frame)) {
            if (key !== 'offset' && key !== 'easing' && key !== 'composite' && key !== 'computedOffset') {
              keyframeProperties.add(key);
            }
          }
        }

        // `effect.getComputedTiming().easing` is the EFFECT's own timing
        // dictionary, which the Web Animations spec sets to `"linear"` for any
        // CSS `animation` (the real per-segment curve lives on each keyframe,
        // not on the effect) — confirmed empirically against a live stack
        // (frontend-engineer, spec 292 T016). With exactly two effective
        // keyframes (one declared, one implicit at the other end — every
        // `sse-*` keyframe here declares only `from` or only `to`), the first
        // keyframe's `easing` is the curve for the single 0→1 segment; the
        // last keyframe's `easing` is unused by the spec and not read here.
        const segmentEasing = keyframes[0]?.easing;

        events.push({
          animationName: animationEvent.animationName,
          pseudoElement: match.effect.pseudoElement ?? null,
          durationMs: typeof timing.duration === 'number' ? timing.duration : null,
          easing: typeof segmentEasing === 'string' ? segmentEasing : null,
          properties: [...keyframeProperties].sort(),
        });
      },
      true,
    );
  });
}

async function recordedAnimations(page: Page): Promise<RecordedAnimation[]> {
  return page.evaluate(() => (window as unknown as { __motionEvents: RecordedAnimation[] }).__motionEvents);
}

async function waitForRecordedAnimation(page: Page, animationName: string): Promise<RecordedAnimation> {
  await expect
    .poll(async () => (await recordedAnimations(page)).some((event) => event.animationName === animationName))
    .toBe(true);

  const match = (await recordedAnimations(page)).find((event) => event.animationName === animationName);
  if (match === undefined) {
    throw new Error(`animation '${animationName}' was not recorded`);
  }
  return match;
}

/**
 * The view-transition case cannot key off `animationName`: it is the user
 * agent's own default cross-fade (not one of the four named
 * `sse-*` keyframes), and Chromium's internal name for it is not a documented
 * contract this test should pin. `pseudoElement` is the documented, stable
 * identifier instead (spec 292 plan.md §7).
 */
async function waitForRecordedAnimationOnPseudoElement(page: Page, pseudoElement: string): Promise<RecordedAnimation> {
  await expect
    .poll(async () => (await recordedAnimations(page)).some((event) => event.pseudoElement === pseudoElement))
    .toBe(true);

  const match = (await recordedAnimations(page)).find((event) => event.pseudoElement === pseudoElement);
  if (match === undefined) {
    throw new Error(`no recorded animation targets pseudo-element '${pseudoElement}'`);
  }
  return match;
}

test.describe('A dialog enters and leaves (US2)', () => {
  test('enters with travel: opacity and transform only, 200ms, ease-out', async ({ page }) => {
    await installMotionRecorder(page);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();

    const content = await waitForRecordedAnimation(page, 'sse-surface-enter');
    expect(content.durationMs).toBe(200);
    expect(content.easing).toBe('cubic-bezier(0.2, 0, 0, 1)');
    expect(content.properties).toEqual(['opacity', 'transform']);

    const overlay = await waitForRecordedAnimation(page, 'sse-scrim-enter');
    expect(overlay.properties).toEqual(['opacity']);
  });

  test('leaves: 160ms, ease-in, then is removed from the DOM', async ({ page }) => {
    await installMotionRecorder(page);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    await waitForRecordedAnimation(page, 'sse-surface-enter');

    await page.getByRole('button', { name: /cancel/i }).click();

    const exit = await waitForRecordedAnimation(page, 'sse-surface-exit');
    expect(exit.durationMs).toBe(160);
    expect(exit.easing).toBe('cubic-bezier(0.4, 0, 1, 1)');

    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('reduced motion redesigns rather than deletes: same animation, same duration, opacity only', async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await installMotionRecorder(page);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();

    const content = await waitForRecordedAnimation(page, 'sse-surface-enter');
    expect(content.durationMs).toBe(200);
    expect(content.properties).toEqual(['opacity']);
  });
});

test.describe('A route change cross-fades (US3)', () => {
  test('a nav click cross-fades over 200ms, ease-in-out', async ({ page }) => {
    await installMotionRecorder(page);
    await signInAsOperator(page);

    await clickSidebarLink(page, 'Layouts');

    const oldView = await waitForRecordedAnimationOnPseudoElement(page, '::view-transition-old(root)');
    const newView = await waitForRecordedAnimationOnPseudoElement(page, '::view-transition-new(root)');

    for (const view of [oldView, newView]) {
      expect(view.durationMs).toBe(200);
      expect(view.easing).toBe('cubic-bezier(0.4, 0, 0.2, 1)');
    }

    await expect(page).toHaveURL(/\/layouts$/);
  });

  test('reduced motion keeps the cross-fade', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await installMotionRecorder(page);
    await signInAsOperator(page);

    await clickSidebarLink(page, 'Layouts');

    const oldView = await waitForRecordedAnimationOnPseudoElement(page, '::view-transition-old(root)');
    expect(oldView.durationMs).toBe(200);

    await expect(page).toHaveURL(/\/layouts$/);
  });
});

/**
 * T002 — CHARACTERISATION (ADR-0139/ADR-0144). Pins Button's rendered transition
 * timing exactly as it arrives today, through the `--default-transition-duration`/
 * `--default-transition-timing-function` bridge (spec 292 §1). Must be observed
 * green on `develop` *before* T011 migrates `Button.tsx` to `transition-colors
 * duration-state ease-state`, and pass unmodified afterwards — `duration-state`/
 * `ease-state` resolve to the same `--duration-fast`/`--ease-out` scale steps
 * (plan.md §2), so the computed values must not move.
 */
test.describe('Button keeps its rendered timing (spec 292 characterisation)', () => {
  test('computed transition-duration is 0.12s, transition-timing-function is cubic-bezier(0.2, 0, 0, 1)', async ({
    page,
  }) => {
    await signInAsOperator(page);

    const registerButton = page.getByRole('button', { name: /register camera/i });
    await expect(registerButton).toHaveCSS('transition-duration', '0.12s');
    await expect(registerButton).toHaveCSS('transition-timing-function', 'cubic-bezier(0.2, 0, 0, 1)');
  });
});
