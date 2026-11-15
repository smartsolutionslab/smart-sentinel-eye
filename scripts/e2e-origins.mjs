// Spec 497: the suite spells the one origin in e2e/support/origins.ts and no fixed UI port anywhere else. Picked up by `pnpm test:guards`
// through scripts/e2e-origins.test.mjs; no CI step of its own.
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

export const ONE_ORIGIN_FILE = 'e2e/support/origins.ts';

// A fixed port is correct here, and the reason says why. Adding one is a reviewed change. An entry with `match` exempts only the lines that
// contain it; one without exempts the whole file. An entry whose file no longer names a port is reported as stale.
export const PERMANENT = [
  {
    file: 'scripts/wait-for-e2e-stack.sh',
    reason:
      "readiness names the stack's ports: the upstream dev servers the gateway proxies, and the one origin it warms",
  },
  {
    file: 'apps/management-web/vite.config.ts',
    reason: "the dev server's own port, and the remotes' dev ports it proxies to",
  },
  { file: 'apps/management-cameras/vite.config.ts', reason: "the dev server's own port" },
  { file: 'apps/management-event-ingestion/vite.config.ts', reason: "the dev server's own port" },
  { file: 'apps/kiosk-web/vite.config.ts', reason: "the dev server's own port (one app serves both :5174 and :5175)" },
  {
    file: 'apps/management-web/src/app/auth.ts',
    match: "typeof window !== 'undefined'",
    reason: 'the redirect_uri fallback when there is no window, so not a browser',
  },
  {
    file: 'apps/kiosk-web/src/app/auth.ts',
    match: "typeof window !== 'undefined'",
    reason: 'the redirect_uri fallback when there is no window, so not a browser',
  },
  { file: 'scripts/e2e-origins.mjs', reason: 'this guard names the ports it forbids' },
];

// Not yet moved onto the one origin: one entry per file and port with the number of occurrences (`count`, default 1), lowered or deleted by the
// stage that moves them. A new occurrence in a listed file is a violation; fewer occurrences than the count make the entry stale until it is
// lowered. So neither the list nor the count in a listed file can grow.
export const MIGRATION = [
  { file: 'playwright.config.ts', port: '5174', stage: 2 },
  { file: 'e2e/kiosk-comes-back.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/kiosk-identity-herd.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/kiosk-identity-refused.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/kiosk-touch-target.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/rule-switches-a-wall.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/spanning-wall.spec.ts', port: '5174', stage: 2 },
  { file: 'e2e/wall-changes-its-scene.spec.ts', port: '5174', stage: 2, count: 2 },
  { file: 'playwright.config.ts', port: '5175', stage: 3 },
  { file: 'e2e/wall-outlives-its-session.spec.ts', port: '5175', stage: 3 },
  { file: 'e2e/wall-survives-a-lockout.spec.ts', port: '5175', stage: 3 },
  { file: 'e2e/wall-survives-a-process-death.spec.ts', port: '5175', stage: 3 },
  { file: 'e2e/wall-withdrawal.spec.ts', port: '5175', stage: 3, count: 4 },
];

// 5173-5177 are the dev servers, 5180 is the one origin. Not matched inside an identifier, a version or a longer number.
const PORT = /(?<![\w.-])(517[3-7]|5180)(?![\w.-])/g;

// A line that is only a comment is prose about a port, not a hard-coded origin; code with a trailing comment is still scanned.
const COMMENT_LINE = /^\s*(\/\/|\/\*|\*|#)/;

const CODE = /\.(ts|tsx|mts|js|mjs|cjs|json|sh|yml|yaml)$/;
const UNIT_TEST = /\.test\.[cm]?[jt]sx?$/;
const SKIPPED_DIRECTORIES = new Set([
  'node_modules',
  'dist',
  'bin',
  'obj',
  'test-results',
  'playwright-report',
  '.git',
]);

export function isScanned(relativePath) {
  const p = relativePath.replaceAll('\\', '/');
  if (!CODE.test(p) || UNIT_TEST.test(p) || p.split('/').some((segment) => SKIPPED_DIRECTORIES.has(segment))) {
    return false;
  }
  return (
    p.startsWith('e2e/') ||
    p === 'playwright.config.ts' ||
    /^scripts\/[^/]+\.(sh|mjs)$/.test(p) ||
    /^\.github\/workflows\/[^/]+\.ya?ml$/.test(p) ||
    /^apps\/[^/]+\/src\//.test(p) ||
    /^apps\/[^/]+\/vite\.config\.ts$/.test(p)
  );
}

export function readScannedFiles(root) {
  const found = [];
  const walk = (directory) => {
    for (const entry of readdirSync(path.join(root, directory), { withFileTypes: true })) {
      const relative = `${directory}/${entry.name}`;
      if (entry.isDirectory()) {
        if (!SKIPPED_DIRECTORIES.has(entry.name)) walk(relative);
      } else if (isScanned(relative)) {
        found.push({ path: relative, text: readFileSync(path.join(root, relative), 'utf8') });
      }
    }
  };
  for (const directory of ['e2e', 'scripts', '.github/workflows', 'apps']) walk(directory);
  found.push({ path: 'playwright.config.ts', text: readFileSync(path.join(root, 'playwright.config.ts'), 'utf8') });
  return found;
}

export function findHardCodedOrigins(files, allow = { permanent: PERMANENT, migration: MIGRATION }) {
  const permanentEntry = (file) => allow.permanent.find((entry) => entry.file === file);
  const migrationEntry = (file, port) => allow.migration.find((entry) => entry.file === file && entry.port === port);
  const permanentUsed = new Set();
  const seen = new Map();
  const violations = [];

  for (const { path: file, text } of files) {
    const permanent = permanentEntry(file);
    text.split('\n').forEach((lineText, index) => {
      if (COMMENT_LINE.test(lineText)) return;
      for (const match of lineText.matchAll(PORT)) {
        const port = match[1];
        if (port === '5180' && file === ONE_ORIGIN_FILE) continue;
        if (permanent && (!permanent.match || lineText.includes(permanent.match))) {
          permanentUsed.add(file);
          continue;
        }
        const entry = migrationEntry(file, port);
        if (entry) {
          const key = `${file}|${port}`;
          seen.set(key, (seen.get(key) ?? 0) + 1);
          if (seen.get(key) <= (entry.count ?? 1)) continue;
        }
        violations.push({ file, line: index + 1, port, text: lineText });
      }
    });
  }

  const stale = [
    ...allow.migration
      .map((entry) => ({ entry, actual: seen.get(`${entry.file}|${entry.port}`) ?? 0 }))
      .filter(({ entry, actual }) => actual < (entry.count ?? 1))
      .map(({ entry, actual }) => ({
        kind: 'migration',
        file: entry.file,
        port: entry.port,
        stage: entry.stage,
        expected: entry.count ?? 1,
        actual,
      })),
    ...allow.permanent
      .filter((entry) => !permanentUsed.has(entry.file))
      .map((entry) => ({ kind: 'permanent', file: entry.file, reason: entry.reason })),
  ];
  return { violations, stale };
}
