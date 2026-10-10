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

const ABSOLUTE_PATH = /^\//;

const navEntrySchema = z
  .object({
    path: z.string().regex(ABSOLUTE_PATH, 'path must be absolute (start with "/")'),
    label: z.string().min(1),
    icon: z.string().optional(),
    order: z.number(),
    // Empty is refused rather than read as "visible to everyone" (FR-015): a
    // forgotten field must not publish a surface to every account.
    requiredScopes: z.array(z.string().regex(SCOPE_PATTERN)).nonempty().readonly(),
  })
  .readonly();

const navManifestSchema = z
  .object({
    // z.literal(1) rather than z.number(): an unknown schemaVersion is a
    // parse failure, fail-closed, not a value this schema merely disagrees
    // with the shape of (FR-015).
    schemaVersion: z.literal(1),
    remote: z.string().min(1),
    basePath: z.string().regex(ABSOLUTE_PATH, 'basePath must be absolute (start with "/")'),
    entries: z.array(navEntrySchema).readonly(),
  })
  .readonly()
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

// Inferred from the schema, not hand-declared, so the two cannot drift: a
// field added to one and not the other used to need a cast at the one place
// this module handed a parsed value back out.
export type NavEntry = z.infer<typeof navEntrySchema>;
export type NavManifest = z.infer<typeof navManifestSchema>;

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
  return { ok: true, manifest: result.data };
}
