import { test as cleanup, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './sign-in';

/**
 * Archives the layouts an e2e run publishes (issue 1933).
 *
 * The sibling teardown reclaims cameras (#1895); nothing reclaimed layouts, so a
 * long-lived dev database accumulated 20+ of them against the three the scenario
 * simulator seeds. The kiosk picker lists published layouts, so the three real
 * walls end up buried under seventeen throwaways.
 *
 * That is not untidiness either. Every manual verification that needs a kiosk —
 * spec 040's T026, quickstart §2 — starts by picking a wall out of that list, and
 * picking an e2e one gets you a tile whose camera points at a fabricated RTSP
 * address. It cost exactly that during spec 040's T027: the first attempt opened
 * an e2e wall, measured nothing, and the run proved nothing.
 *
 * **Why they accumulate at all is by design.** Layout names are unique per fab,
 * so a fixed name would collide on a second run against a surviving database —
 * `seed-published-layout.setup.ts` says so. The timestamp that avoids the
 * collision is what makes each run leave one behind.
 *
 * **Matches by name**, like the camera teardown and for the same reasons: it
 * needs no registry threaded through the specs, and it cleans the residue that
 * is already there on its first run.
 *
 * **Archives rather than deletes**, because archiving is what the product does.
 * It targets the live revision, which is the one the picker lists — a chain with
 * only a draft was never on the picker and is left alone.
 */
const DISPOSABLE = /^(E2E Layout |E2E Race Layout |Kiosk Seed Wall |SC004 Wall |Spec056 Wall )/;

/**
 * Stop working after this long and report the remainder — the same reasoning as
 * the camera sweep: the residue is unbounded, so bound the clock rather than the
 * attempts, and never redden a green run over a tidy-up.
 */
const DEADLINE_MS = 5 * 60 * 1000;

cleanup('archive the layouts this run published', async ({ page }) => {
  cleanup.setTimeout(600_000);
  const deadline = Date.now() + DEADLINE_MS;

  await signInAsOperator(page);

  const attempted = new Set<string>();
  const skipped: string[] = [];
  let archived = 0;
  let outOfTime = false;

  for (let sweep = 0; sweep < 40 && !outOfTime; sweep++) {
    const targets = (await disposableNames(page)).filter((name) => !attempted.has(name));
    if (targets.length === 0) break;

    for (const name of targets) {
      if (Date.now() > deadline) {
        outOfTime = true;
        break;
      }
      attempted.add(name);

      const outcome = await archiveByName(page, name);
      if (outcome === 'archived') {
        archived++;
        continue;
      }

      // "Nothing to archive" is not a failure and must not trigger a re-auth: a
      // chain with only a draft was never on the picker. Conflating the two sent
      // the first version of this into signInAsOperator on an already-signed-in
      // page, where it waited for a "Sign in" button that does not exist until
      // the whole test timed out — 11.5 minutes, and nothing archived.
      if (outcome === 'nothing-to-do') {
        continue;
      }

      // A genuine refusal is not taken at face value: this job outlives
      // Keycloak's 300 s default token lifespan, and a lapsed session looks like
      // a missing row. The camera teardown documents the same remedy.
      if (await looksSignedOut(page)) await signInAsOperator(page);
      if ((await archiveByName(page, name)) === 'archived') archived++;
      else skipped.push(name);
    }
  }

  console.log(`[cleanup] archived ${archived} layout(s); skipped ${skipped.length}`);
  for (const name of skipped) console.log(`[cleanup]   skipped ${name}`);

  if (outOfTime) {
    const left = (await disposableNames(page)).length;
    console.log(
      `[cleanup] stopped on the ${DEADLINE_MS / 60_000}-minute deadline with at least ` +
        `${left} still matching — a later run continues where this one left off`,
    );
  }
});

/** Names of the layouts on the page whose names mark them as e2e residue. */
async function disposableNames(page: Page): Promise<string[]> {
  await page.goto('/layouts');
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  // The heading is static, so it proves nothing about the rows. Reading them
  // before they render returns zero matches and looks exactly like a clean
  // database — the failure mode that made the camera teardown a silent no-op on
  // its first run.
  await expect(page.getByText('Loading…')).toHaveCount(0);

  const names: string[] = [];
  for (const heading of await page.getByRole('heading', { level: 2 }).all()) {
    const text = (await heading.innerText()).trim();
    if (DISPOSABLE.test(text)) names.push(text);
  }
  return names;
}

/** Whether the shell is showing the unauthenticated screen. */
async function looksSignedOut(page: Page): Promise<boolean> {
  return (await page.getByRole('button', { name: /sign in/i }).count()) > 0;
}

type Outcome = 'archived' | 'nothing-to-do' | 'refused';

/**
 * Archives one layout's live revision.
 *
 * Every interaction carries an explicit timeout. Playwright's default action
 * timeout is unbounded, so without them a single unactionable element does not
 * fail this row — it consumes the whole test budget and the sweep archives
 * nothing, which is how the first run of this file spent 11.5 minutes.
 */
async function archiveByName(page: Page, name: string): Promise<Outcome> {
  const row = page.getByRole('listitem').filter({ hasText: name }).first();
  const archive = row.getByRole('button', { name: /^archive$/i });

  // Only a chain with a live revision offers Archive, and only a live revision
  // is on the kiosk picker. A draft-only chain is not residue worth chasing.
  if ((await archive.count()) === 0) return 'nothing-to-do';

  try {
    await archive.click({ timeout: 15_000 });
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: /^archive$/i })
      .click({ timeout: 15_000 });
    await expect(row.getByRole('button', { name: /^archive$/i })).toHaveCount(0, { timeout: 20_000 });
    return 'archived';
  } catch {
    return 'refused';
  }
}
