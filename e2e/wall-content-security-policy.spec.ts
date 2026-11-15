import { test, expect } from '@playwright/test';
import { signInAsWallDisplay } from './support/wall-session';
import { ONE_ORIGIN, WALL_PATH } from './support/origins';
import { PolicyViolationRecorder } from './support/policy-violations';

test.use({ baseURL: ONE_ORIGIN });

test('the wall display is served with a content security policy and raises no violation, signed in', async ({
  page,
}) => {
  test.setTimeout(120_000);
  const recorder = await PolicyViolationRecorder.attach(page);

  const policy = await recorder.goto(WALL_PATH);
  expect(policy, 'the wall display document should carry a Content-Security-Policy header').toBeTruthy();
  recorder.expectNone('on the signed-out wall display');

  await signInAsWallDisplay(page, { expectPopulatedPicker: true });
  recorder.expectNone('through sign-in');

  await page.getByRole('listitem').first().getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();
  recorder.expectNone('on a wall');
});
