import { test as cleanup, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './sign-in';

/**
 * Retires the cameras an e2e run registers (issue 1895).
 *
 * The specs register cameras and nothing removed them, so a long-lived dev
 * database accumulated ~106 of them against 4 real ones. Every e2e camera points
 * at a fabricated RTSP address that nothing serves, so each shows "Stream is
 * offline" — and with the list defaulting to newest-first, the cameras that do
 * stream end up pages deep behind a wall of dead ones.
 *
 * That is not untidiness. Checking live video by eye is the one claim no
 * automated check in this repo can make, and it starts by opening a camera from
 * the list. A list where the first page is uniformly offline makes a working
 * feature look broken, which cost spec 043's verification exactly that detour.
 *
 * **Matches by name rather than tracking registrations.** The alternative — a
 * shared helper threading a registry through six call sites in four files —
 * changes more code to do the same thing, and would not touch the residue
 * already there. These prefixes are what the specs already name their cameras,
 * so this cleans historical rows on its first run too.
 *
 * **Retires rather than deletes**, because retiring is what the product does: the
 * record survives and the row leaves the default listing, which is the property
 * that matters here.
 *
 * **Best-effort, and says so.** A camera it cannot retire is counted and named
 * at the end rather than failing the suite — a teardown that reddens a green run
 * over one uncooperative row would get deleted, and then nothing cleans up. What
 * it must not do is skip silently, so both totals are always logged.
 */
const DISPOSABLE = /^(E2E |Kiosk Seed Cam |Push Probe Cam |T012 Verification )/;

/**
 * Stop working after this long and report the remainder.
 *
 * Retiring goes through the UI at roughly five seconds a camera, so the time a
 * sweep takes is set by how much residue it finds — which is unbounded. A first
 * run against a 90-camera backlog ran 16.3 minutes and **failed on the test
 * timeout**, turning a best-effort tidy-up into a red suite. Bounding attempts
 * would not have helped: the cost per camera is what varies.
 *
 * So the work is bounded by the clock and the leftovers are named. A backlog
 * drains over a few runs; the steady state — a handful of cameras per run —
 * finishes in well under a minute.
 */
const DEADLINE_MS = 8 * 60 * 1000;

cleanup('retire the cameras this run registered', async ({ page }) => {
  cleanup.setTimeout(900_000);
  const deadline = Date.now() + DEADLINE_MS;

  await signInAsOperator(page);

  // Retiring removes a camera from the default listing, so page one refills from
  // behind and this converges without paging. `attempted` is what makes it
  // terminate: a camera that cannot be retired stays on page one, and without it
  // every sweep would pick the same row up again.
  const attempted = new Set<string>();
  const skipped: string[] = [];
  let retired = 0;
  let outOfTime = false;

  for (let sweep = 0; sweep < 40 && !outOfTime; sweep++) {
    const targets = (await disposableOnFirstPage(page)).filter((href) => !attempted.has(href));
    if (targets.length === 0) break;

    for (const href of targets) {
      if (Date.now() > deadline) {
        outOfTime = true;
        break;
      }
      attempted.add(href);

      if (await retireAt(page, href)) {
        retired++;
        continue;
      }

      // "No such camera" is also what a lapsed session looks like, and this job
      // outlives one: the realm sets no accessTokenLifespan, so Keycloak's 300 s
      // default applies while a sweep of ~90 cameras takes longer. Two runs each
      // retired ~25 and then found every remaining camera "missing", at about
      // the five-minute mark.
      //
      // Not an application defect: `gateway.ts` gives a 401 exactly one silent
      // renewal and one retry (spec 011 FR-011/012). That renewal is what does
      // not complete here — a `prompt=none` iframe against a cross-origin
      // Keycloak on a self-signed certificate — so this is an artefact of the
      // test environment, not of the console.
      //
      // Either way a refusal is not taken at face value: sign in again and ask
      // once more. Only a camera that refuses on a fresh session is skipped.
      await signInAsOperator(page);
      if (await retireAt(page, href)) retired++;
      else skipped.push(href);
    }
  }

  console.log(`[cleanup] retired ${retired} camera(s); skipped ${skipped.length}`);
  for (const href of skipped) console.log(`[cleanup]   skipped ${href}`);

  if (outOfTime) {
    const left = (await disposableOnFirstPage(page)).length;
    console.log(
      `[cleanup] stopped on the ${DEADLINE_MS / 60_000}-minute deadline with at least ` +
        `${left} still matching — a later run continues where this one left off`,
    );
  }
});

/** Hrefs of the cameras on page one whose names mark them as e2e residue. */
async function disposableOnFirstPage(page: Page): Promise<string[]> {
  await page.goto('/cameras');
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();

  // The heading is static, so waiting on it proves nothing about the rows —
  // DataTable renders a "Loading…" row first and the links appear later. Reading
  // the table before that returns zero matches and looks exactly like a clean
  // database, which would make this whole file a no-op nobody noticed. A first
  // run did precisely that and passed.
  await expect(page.getByText('Loading…')).toHaveCount(0);

  const hrefs: string[] = [];
  for (const link of await page.getByRole('link').filter({ hasText: DISPOSABLE }).all()) {
    const href = await link.getAttribute('href');
    if (href !== null && href.startsWith('/cameras/')) hrefs.push(href);
  }
  return hrefs;
}

/**
 * Retires one camera, or returns false if it could not. False covers two cases
 * that are deliberately indistinguishable from here: a camera already retired
 * (spec 032's retire test leaves one behind), and one the page refuses to show
 * at all. Spec 029 makes every refusal render the same sentence on purpose —
 * "No such camera" is what a deleted camera, another fab's camera and an expired
 * token all look like — so this cannot narrate a cause it does not have.
 */
async function retireAt(page: Page, href: string): Promise<boolean> {
  await page.goto(href);

  // Same trap as the listing, one level down, and it bit: `count()` resolves
  // immediately, so checking it while the detail page still shows "Loading…"
  // finds no Retire button and skips the camera as already-retired. A run
  // retired 2 of ~90 that way and passed, because "no control here" and "not
  // loaded yet" are the same observation to `count()`.
  await expect(page.getByText('Loading…')).toHaveCount(0);

  if ((await page.getByRole('heading', { name: /no such camera/i }).count()) > 0) return false;

  const retire = page.getByRole('button', { name: /retire camera/i });
  if ((await retire.count()) === 0) return false;

  await retire.click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: /retire camera/i })
    .click();

  // The session can lapse between opening the page and confirming, so this
  // reports rather than asserts — the caller re-authenticates and retries, and
  // a hard expectation here would fail the run on a recoverable condition.
  return await page
    .getByRole('status')
    .filter({ hasText: /retired/i })
    .waitFor({ timeout: 15_000 })
    .then(() => true)
    .catch(() => false);
}
