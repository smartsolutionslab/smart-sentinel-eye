import {
  test,
  expect,
  type Page,
  type Request as PlaywrightRequest,
  type Response as PlaywrightResponse,
} from '@playwright/test';

/**
 * Spec 261 (issue #2333), plan.md §6.4 facts E1-E4 — the running-page
 * authority for the self-hosted font pipeline. Runs on management-web's
 * unauthenticated sign-in screen (spec §1 finding 5: a SemiBold `<h1>` and a
 * Regular button), so no Keycloak round-trip is needed. A fresh context per
 * test gives a cold cache; network listeners attach before `page.goto('/')`
 * so no request is missed.
 *
 * Red on develop (spec 261 §6): E2-E4, and E1's second clause. E1's first
 * clause ("every font request is same-origin") is a declared vacuous-green
 * pin — nothing fetches a font at all yet, so it holds trivially — paired in
 * the same test with the red second clause, so the pin is never asserted on
 * its own.
 *
 * Waits are by condition (`document.fonts.ready`, `expect.poll`), never by a
 * fixed count or timeout (ADR-0150).
 */

interface FontNetworkActivity {
  requests: PlaywrightRequest[];
  responses: PlaywrightResponse[];
}

/** Attaches font-request/response listeners before the caller navigates. */
function trackFontRequests(page: Page): FontNetworkActivity {
  const activity: FontNetworkActivity = { requests: [], responses: [] };

  page.on('request', (request) => {
    if (request.resourceType() === 'font') {
      activity.requests.push(request);
    }
  });
  page.on('response', (response) => {
    if (response.request().resourceType() === 'font') {
      activity.responses.push(response);
    }
  });

  return activity;
}

async function gotoSignInScreen(page: Page): Promise<void> {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: /Smart Sentinel Eye/i })).toBeVisible();
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
}

test.describe('self-hosted fonts (spec 261, issue #2333)', () => {
  test('every font request goes to the page’s own origin, and a Plex Sans Regular Latin1 woff2 loads (E1)', async ({
    page,
  }) => {
    const activity = trackFontRequests(page);

    await gotoSignInScreen(page);

    const pageOrigin = new URL(page.url()).origin;
    const offOrigin = activity.requests.filter((request) => new URL(request.url()).origin !== pageOrigin);

    // Declared vacuous-green pin (spec §6): nothing fetches a font yet, so
    // this holds trivially on develop. Paired here with the red second
    // clause below, so the pin is never asserted alone.
    expect(
      offOrigin.map((request) => request.url()),
      'every font request must go to the page’s own origin',
    ).toEqual([]);

    await expect
      .poll(
        () =>
          activity.responses.some(
            (response) => response.url().includes('IBMPlexSans-Regular-Latin1') && response.status() === 200,
          ),
        {
          message: 'no IBMPlexSans-Regular-Latin1 woff2 request finished 200 — the page fetched no such font at all',
        },
      )
      .toBe(true);
  });

  test('once fonts are ready, the body computes IBM Plex Sans and both weights are usable (E2)', async ({ page }) => {
    await gotoSignInScreen(page);

    const result = await page.evaluate(() => ({
      bodyFontFamily: getComputedStyle(document.body).fontFamily,
      regularUsable: document.fonts.check('400 16px "IBM Plex Sans"'),
      semiboldUsable: document.fonts.check('600 16px "IBM Plex Sans"'),
    }));

    expect(result.bodyFontFamily, 'document.body’s computed font-family').toMatch(/^"?IBM Plex Sans"?/);
    expect(result.regularUsable, 'document.fonts.check(\'400 16px "IBM Plex Sans"\') must be true').toBe(true);
    expect(result.semiboldUsable, 'document.fonts.check(\'600 16px "IBM Plex Sans"\') must be true').toBe(true);
  });

  test('exactly two font preload links exist, each resolving to a font the page requested exactly once (E3)', async ({
    page,
  }) => {
    const activity = trackFontRequests(page);

    await gotoSignInScreen(page);

    const preloadHrefs = await page.evaluate(() =>
      [...document.querySelectorAll('link[rel="preload"][as="font"]')].map((link) => (link as HTMLLinkElement).href),
    );

    expect(preloadHrefs.length, `expected exactly 2 <link rel="preload" as="font">, found ${preloadHrefs.length}`).toBe(
      2,
    );

    for (const href of preloadHrefs) {
      await expect
        .poll(() => activity.requests.filter((request) => request.url() === href).length, {
          message: `preloaded URL ${href} must be requested exactly once`,
        })
        .toBe(1);
    }
  });

  const fallbackPairs = [
    { label: 'Sans 400', plexFamily: 'IBM Plex Sans', fallbackFamily: 'IBM Plex Sans Fallback', weight: 400 },
    { label: 'Sans 500', plexFamily: 'IBM Plex Sans', fallbackFamily: 'IBM Plex Sans Fallback', weight: 500 },
    { label: 'Sans 600', plexFamily: 'IBM Plex Sans', fallbackFamily: 'IBM Plex Sans Fallback', weight: 600 },
    { label: 'Mono 400', plexFamily: 'IBM Plex Mono', fallbackFamily: 'IBM Plex Mono Fallback', weight: 400 },
  ] as const;

  const PROSE_SAMPLE = 'This screen is no longer authorized';
  const DIGIT_SAMPLE = '12:34:56.789';

  for (const { label, plexFamily, fallbackFamily, weight } of fallbackPairs) {
    test(`the metric-matched fallback keeps Plex’s box for ${label} (E4)`, async ({ page }) => {
      await gotoSignInScreen(page);

      const loaded = await page.evaluate(
        async ({ plexFamily, fallbackFamily, weight }) => {
          await Promise.all([
            document.fonts.load(`${weight} 16px "${plexFamily}"`),
            document.fonts.load(`${weight} 16px "${fallbackFamily}"`),
          ]);
          return {
            plexLoaded: document.fonts.check(`${weight} 16px "${plexFamily}"`),
            // `document.fonts.check()` returns true even when nothing matches
            // (it just means the browser would use its own default font), so
            // a mistyped fallback family would still pass that gate. Require
            // an actual loaded FontFace of that family instead (N6).
            fallbackLoaded: [...document.fonts].some(
              (face) => face.family.replace(/^["']|["']$/g, '') === fallbackFamily && face.status === 'loaded',
            ),
          };
        },
        { plexFamily, fallbackFamily, weight },
      );

      // Fails loudly rather than silently comparing against the browser's own
      // default font (plan.md R3): a machine with neither Arial nor
      // Liberation/Arimo installed — or, on develop, a fallback face that
      // does not exist yet — cannot be measured against.
      expect(
        loaded.fallbackLoaded,
        `"${fallbackFamily}" at weight ${weight} did not resolve to a loaded face — no Arial/Liberation on this machine, or the fallback is not declared yet`,
      ).toBe(true);
      expect(loaded.plexLoaded, `"${plexFamily}" at weight ${weight} did not resolve to a loaded face`).toBe(true);

      const measurements = await page.evaluate(
        ({ plexFamily, fallbackFamily, weight, proseSample, digitSample }) => {
          function measure(
            fontFamily: string,
            text: string,
          ): { height: number; width: number; baselineOffset: number } {
            const container = document.createElement('div');
            container.style.cssText =
              'position:fixed; left:-99999px; top:0; margin:0; padding:0; border:0; white-space:nowrap; ' +
              `line-height:normal; font-size:16px; font-weight:${weight}; font-family:"${fontFamily}";`;

            const textSpan = document.createElement('span');
            textSpan.textContent = text;

            // A zero-size inline-block aligned to `baseline` sits exactly on
            // the line's baseline, so its top edge locates the baseline.
            const marker = document.createElement('span');
            marker.style.cssText = 'display:inline-block; width:0; height:0; vertical-align:baseline;';

            container.append(textSpan, marker);
            document.body.appendChild(container);

            const containerRect = container.getBoundingClientRect();
            const textRect = textSpan.getBoundingClientRect();
            const markerRect = marker.getBoundingClientRect();

            container.remove();

            return {
              height: containerRect.height,
              width: textRect.width,
              baselineOffset: markerRect.top - containerRect.top,
            };
          }

          return {
            plexProse: measure(plexFamily, proseSample),
            fallbackProse: measure(fallbackFamily, proseSample),
            plexDigits: measure(plexFamily, digitSample),
            fallbackDigits: measure(fallbackFamily, digitSample),
          };
        },
        { plexFamily, fallbackFamily, weight, proseSample: PROSE_SAMPLE, digitSample: DIGIT_SAMPLE },
      );

      for (const [sampleLabel, plexBox, fallbackBox] of [
        ['prose', measurements.plexProse, measurements.fallbackProse],
        ['digits', measurements.plexDigits, measurements.fallbackDigits],
      ] as const) {
        expect(
          Math.abs(plexBox.height - fallbackBox.height),
          `${label} ${sampleLabel}: line-box height differs by more than 1px (Plex ${plexBox.height}, fallback ${fallbackBox.height})`,
        ).toBeLessThanOrEqual(1);

        expect(
          Math.abs(plexBox.baselineOffset - fallbackBox.baselineOffset),
          `${label} ${sampleLabel}: baseline offset differs by more than 1px (Plex ${plexBox.baselineOffset}, fallback ${fallbackBox.baselineOffset})`,
        ).toBeLessThanOrEqual(1);

        const widthDeltaPercent = (Math.abs(plexBox.width - fallbackBox.width) / plexBox.width) * 100;
        expect(
          widthDeltaPercent,
          `${label} ${sampleLabel}: width differs by more than 3% (Plex ${plexBox.width}, fallback ${fallbackBox.width})`,
        ).toBeLessThanOrEqual(3);
      }
    });
  }
});
