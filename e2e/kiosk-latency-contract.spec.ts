import { test, expect } from '@playwright/test';
import { openFirstLayout, signInToKiosk } from './support/kiosk-session';

/**
 * Spec 045 T018 — the kiosk-latency contract, exercised by a real kiosk.
 *
 * **Why this is an e2e test and not an integration test.** The endpoint's
 * closed-set validation sits *behind* the `IsBrowserKiosk()` gate: a non-kiosk
 * principal is accepted and dropped before `Measurement` is ever looked at
 * (#1893). So reaching the validation at all needs a token whose `azp` is
 * `kiosk-web` — and `kiosk-web` is a **public client with
 * `directAccessGrantsEnabled: false`**, so there is no password grant and no
 * client-credentials grant to mint one with. The only way to hold that token is
 * to be a kiosk that signed in, which is what this file is.
 *
 * The alternative was enabling direct-access grants on the kiosk client to suit
 * a test, which loosens production auth for the convenience of the suite.
 *
 * **The origin is discovered, not assumed.** The gateway URL is baked into the
 * bundle from `VITE_API_GATEWAY_URL` at build time, so the test reads it off a
 * request the app actually makes rather than hard-coding a guess that would rot.
 */

/** Reads the access token the kiosk is holding, as the app itself would. */
async function kioskBearerToken(page: import('@playwright/test').Page): Promise<string> {
  const token = await page.evaluate(() => {
    const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const stored: unknown = JSON.parse(window.sessionStorage.getItem(key) ?? 'null');
    const accessToken = (stored as { access_token?: unknown } | null)?.access_token;
    return typeof accessToken === 'string' ? accessToken : null;
  });

  expect(token, 'the kiosk should be holding an access token').not.toBeNull();
  return token as string;
}

test('the kiosk-latency contract accepts its five names and refuses anything else', async ({ page }) => {
  // Capture the gateway origin from a call the app makes on its own.
  const gatewayRequest = page.waitForRequest((request) =>
    /\/(layout-composition|stream-distribution|overlay-designer|system-variables)\//.test(request.url()),
  );

  await signInToKiosk(page);
  await openFirstLayout(page);

  const gatewayOrigin = new URL((await gatewayRequest).url()).origin;
  const token = await kioskBearerToken(page);
  const camera = '0198f2c1-0000-7000-8000-000000000001';

  const post = async (measurement: string, cameraIdentifier = camera) =>
    page.evaluate(
      async ([origin, bearer, name, cam]) => {
        const response = await fetch(`${origin}/stream-distribution/streams/kiosk-latency`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${bearer}` },
          body: JSON.stringify({ measurement: name, camera: cam, elapsedMilliseconds: 42.5 }),
        });
        return { status: response.status, body: await response.text() };
      },
      [gatewayOrigin, token, measurement, cameraIdentifier] as const,
    );

  // The two spec 040 had, the two spec 045 added, and spec 046's hold. All five
  // are accepted, and 202 rather than 200 because nothing is read back.
  //
  // `KioskMeasurementContractTests` already compares the client union to the
  // server switch by parsing both. This is the other half of the same claim and
  // the half that cannot be faked: a real kiosk token against a real endpoint,
  // where a name the server refuses shows as a 400 rather than as two files
  // agreeing with each other.
  for (const measurement of ['presentation_buffer', 'wall_skew', 'overlay_draw', 'receive_to_decoded', 'label_delay']) {
    const { status } = await post(measurement);
    expect(status, `${measurement} should be accepted`).toBe(202);
  }

  // The closed set still closes. This is the assertion that fails if someone
  // relaxes the switch to a catch-all, which would let a typo'd name vanish
  // into a metric nobody reads.
  const unknown = await post('buffer');
  expect(unknown.status, 'an unrecognised measurement must be refused').toBe(400);
  expect(unknown.body).toContain('presentation_buffer');
  expect(unknown.body, 'the message should name every accepted value').toContain('wall_skew');
  expect(unknown.body, 'including the one spec 046 added').toContain('label_delay');

  // A report that does not name its tile is refused: a wall reporting one
  // blended figure hides the single tile that is out (#1931).
  const anonymous = await post('presentation_buffer', '00000000-0000-0000-0000-000000000000');
  expect(anonymous.status, 'a report naming no camera must be refused').toBe(400);
});
