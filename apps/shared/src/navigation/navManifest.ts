import { z } from 'zod';

/**
 * Spec 316 FR-015, plan.md §2.1 — the nav-manifest contract every remote
 * publishes as a static `nav-manifest.json` beside its federation entry, and
 * the in-shell entries declare in shell code (`shellEntries.ts`). Readable
 * without loading the remote's code: the shell must be able to decide NOT to
 * fetch an unentitled remote's bundle (ADR-0168 §4), which a module export
 * could never allow since importing it would already mean fetching it.
 */
export const SCOPE_PATTERN = /^sse\.[a-z]+(\.[a-z]+)+$/;

const navEntrySchema = z.object({
  path: z.string().min(1),
  label: z.string().min(1),
  icon: z.string().optional(),
  order: z.number(),
  // Empty is refused rather than read as "visible to everyone" (FR-015): a
  // forgotten field must not publish a surface to every account.
  requiredScopes: z.array(z.string().regex(SCOPE_PATTERN)).nonempty(),
});

const navManifestSchema = z
  .object({
    // z.literal(1) rather than z.number(): an unknown schemaVersion is a
    // parse failure, fail-closed, not a value this schema merely disagrees
    // with the shape of (FR-015).
    schemaVersion: z.literal(1),
    remote: z.string().min(1),
    basePath: z.string().min(1),
    entries: z.array(navEntrySchema),
  })
  .superRefine((manifest, ctx) => {
    for (const [index, entry] of manifest.entries.entries()) {
      if (entry.path !== manifest.basePath && !entry.path.startsWith(`${manifest.basePath}/`)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['entries', index, 'path'],
          message: `entry path "${entry.path}" is outside basePath "${manifest.basePath}"`,
        });
      }
    }
  });

export type NavEntry = {
  readonly path: string;
  readonly label: string;
  readonly icon?: string;
  readonly order: number;
  readonly requiredScopes: readonly [string, ...string[]];
};

export type NavManifest = {
  readonly schemaVersion: 1;
  readonly remote: string;
  readonly basePath: string;
  readonly entries: readonly NavEntry[];
};

export type NavManifestParseResult =
  { readonly ok: true; readonly manifest: NavManifest } | { readonly ok: false; readonly reason: string };

/**
 * Parses and validates a remote's (or a test's) nav manifest. Returns a
 * discriminated union rather than throwing, so a caller (`NavigationProvider`)
 * can log the rejection's `reason` through `logResilienceEvent('navigation',
 * 'manifest-rejected', { remote, reason })` without wrapping every manifest
 * fetch in a try/catch.
 */
export function parseNavManifest(input: unknown): NavManifestParseResult {
  const result = navManifestSchema.safeParse(input);
  if (!result.success) {
    return { ok: false, reason: result.error.issues.map((issue) => issue.message).join('; ') };
  }
  return { ok: true, manifest: result.data as unknown as NavManifest };
}
