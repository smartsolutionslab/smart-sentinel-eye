import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { ONE_ORIGIN } from './support/origins';
import { PolicyViolationRecorder } from './support/policy-violations';

// The file name must not contain "wall-" or "kiosk-": the config's patterns are unanchored, so either one sends a spec to a kiosk
// project and hides it from the console's. The kiosk's and the wall display's own runs are in the files named for them.
test.use({ baseURL: ONE_ORIGIN });

test('the console is served with a content security policy and raises no violation', async ({ page }) => {
  const recorder = await PolicyViolationRecorder.attach(page);

  const policy = await recorder.goto('/');
  await page.waitForLoadState('load');

  expect(policy, 'the console document should carry a Content-Security-Policy header').toBeTruthy();
  recorder.expectNone('on the signed-out console');
});

test('the signed-in console raises no violation on its cameras page', async ({ page }) => {
  test.setTimeout(120_000);
  const recorder = await PolicyViolationRecorder.attach(page);

  await signInAsOperator(page);
  recorder.expectNone('through sign-in');

  const policy = await recorder.goto('/cameras');
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();

  expect(policy, 'the cameras document should carry a Content-Security-Policy header').toBeTruthy();
  recorder.expectNone('on the cameras page');
});
