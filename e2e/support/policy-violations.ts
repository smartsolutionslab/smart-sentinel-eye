import { expect, type Page } from '@playwright/test';

import { ONE_ORIGIN } from './origins';

export interface PolicyViolation {
  documentURL: string;
  violatedDirective: string;
  blockedURI: string;
}

/**
 * Collects every `securitypolicyviolation` the one origin's documents raise, in this Node process.
 *
 * <para>
 * The list lives here and not in the page: a page-side array is discarded with its document, so a violation raised before a
 * navigation (the sign-in redirect, the silent renew) would be gone by the time anything read it. The context binding and init script
 * apply to every document and frame the context opens; documents of other origins (the identity provider's own pages) are not the
 * gateway's policy to answer for and are skipped.
 * </para>
 */
export class PolicyViolationRecorder {
  private readonly seen: PolicyViolation[] = [];

  private constructor(private readonly page: Page) {}

  static async attach(page: Page): Promise<PolicyViolationRecorder> {
    const recorder = new PolicyViolationRecorder(page);
    const context = page.context();

    await context.exposeBinding('reportPolicyViolation', (_source, violation: PolicyViolation) => {
      recorder.seen.push(violation);
    });
    await context.addInitScript((origin: string) => {
      if (window.location.origin !== origin) return;
      document.addEventListener('securitypolicyviolation', (event) => {
        (window as unknown as { reportPolicyViolation: (violation: PolicyViolation) => void }).reportPolicyViolation({
          documentURL: event.documentURI,
          violatedDirective: event.violatedDirective,
          blockedURI: event.blockedURI,
        });
      });
    }, ONE_ORIGIN);

    return recorder;
  }

  /** Reads the list before leaving a document, so a violation is attributed to the page that raised it. */
  async goto(path: string): Promise<string | undefined> {
    this.expectNone(`before navigating to ${path}`);
    const response = await this.page.goto(path);
    return response?.headers()['content-security-policy'];
  }

  expectNone(when: string): void {
    expect(this.seen, `no content security policy violation should have been raised ${when}`).toEqual([]);
  }
}
