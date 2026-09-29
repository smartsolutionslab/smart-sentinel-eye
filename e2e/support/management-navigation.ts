import { expect, type Page } from '@playwright/test';

/**
 * Spec 288 US2 (ADR-0162 §1-2) — the sidebar navigate-and-arrive pair,
 * extracted from the 63 sites that re-typed it (spec 288 §1.2): 51 pairs of
 * "click the section link, wait for its heading" plus 12 lone clicks whose
 * own trailing assertion is what the test proves (a focus check, an
 * `aria-current` check, a two-context race) and stays in the spec file.
 *
 * Every export here takes the `Page` it drives, exactly like
 * `signInAsOperator`/`signInToKiosk`, so a site driving a second context
 * (`pageOne`/`pageTwo`, `admin`, `operatorPage`) uses it the same way a site
 * on the test's own `page` does (FR-003).
 */

export type ConsoleSection = 'Cameras' | 'Layouts' | 'Walls' | 'Overlays' | 'System variables' | 'Rules' | 'Audit';

const SIDEBAR_LINK_PATTERN: Record<ConsoleSection, RegExp> = {
  Cameras: /^cameras$/i,
  Layouts: /^layouts$/i,
  Walls: /^walls$/i,
  Overlays: /^overlays$/i,
  'System variables': /^system variables$/i,
  Rules: /^rules$/i,
  Audit: /^audit$/i,
};

/**
 * Clicks the sidebar link for `section`. Asserts nothing — used where the
 * site's own following assertion (focus, `aria-current`, a second context's
 * race) is the thing the test proves, not mere arrival.
 */
export async function clickSidebarLink(page: Page, section: ConsoleSection): Promise<void> {
  await page.getByRole('link', { name: SIDEBAR_LINK_PATTERN[section] }).click();
}

/**
 * Clicks the sidebar link for `section` and waits for its heading to render
 * — the pair every plain "go to section X" site used. `exact: true`
 * throughout: every one of the 51 real callers used it, with no variance
 * (spec 288 §1.2), so it is not a parameter (ADR-0036).
 */
export async function openSection(page: Page, section: ConsoleSection): Promise<void> {
  await clickSidebarLink(page, section);
  await expect(page.getByRole('heading', { name: section, exact: true })).toBeVisible();
}
