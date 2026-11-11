// Emits, for every relative module specifier under apps/*/src, the file TypeScript (the app's real options) resolves it to.
// usage: node resolve-snapshot.mjs > snapshot.json ; compare two snapshots with `node resolve-snapshot.mjs --diff a.json b.json`
import { readFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { APPS, isRelative, moduleLiterals, parse, sourceFiles, ts } from './specifiers.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const rel = (p) => relative(root, p).replaceAll('\\', '/');

if (process.argv[2] === '--diff') {
  const [a, b] = process.argv.slice(3).map((f) => JSON.parse(readFileSync(f, 'utf8')));
  const key = (e) => `${e.file}#${e.n}`;
  const left = new Map(a.entries.map((e) => [key(e), e]));
  let differ = 0;
  for (const e of b.entries) {
    const before = left.get(key(e));
    if (before === undefined || before.resolved !== e.resolved) {
      differ++;
      if (differ <= 10) console.log(`DIFF ${key(e)}: ${before?.resolved} -> ${e.resolved}`);
    }
  }
  const sizeOk = a.entries.length === b.entries.length;
  console.log(
    `before ${a.entries.length}, after ${b.entries.length}, differing ${differ}, unresolved before/after ${a.unresolved}/${b.unresolved}`,
  );
  process.exit(differ === 0 && sizeOk ? 0 : 1);
}

const entries = [];
let unresolved = 0;
for (const app of APPS) {
  const dir = join(root, 'apps', app);
  const read = ts.readConfigFile(join(dir, 'tsconfig.json'), ts.sys.readFile);
  const options = ts.parseJsonConfigFileContent(read.config, ts.sys, dir).options;
  for (const file of sourceFiles(join(dir, 'src'))) {
    let n = 0;
    for (const literal of moduleLiterals(parse(file, readFileSync(file, 'utf8')))) {
      if (!isRelative(literal.text)) continue;
      const resolved = ts.resolveModuleName(literal.text, file, options, ts.sys).resolvedModule?.resolvedFileName;
      if (resolved === undefined) unresolved++;
      entries.push({ file: rel(file), n: n++, resolved: resolved === undefined ? null : rel(resolved) });
    }
  }
}
console.log(JSON.stringify({ unresolved, entries }));
