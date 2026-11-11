// Removes a trailing `.js` from relative module specifiers under apps/*/src. Idempotent. Nothing else in a file is touched.
// usage: node codemod.mjs [--write] [app ...]      (no --write: a dry run that only reports)
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { APPS, isRelative, moduleLiterals, parse, sourceFiles } from './specifiers.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const args = process.argv.slice(2);
const write = args.includes('--write');
const apps = args.filter((a) => !a.startsWith('--'));
let changed = 0;
let remaining = 0;
let files = 0;

for (const app of apps.length > 0 ? apps : APPS) {
  for (const file of sourceFiles(join(root, 'apps', app, 'src'))) {
    const text = readFileSync(file, 'utf8');
    const edits = [];
    for (const literal of moduleLiterals(parse(file, text))) {
      if (isRelative(literal.text) && literal.text.endsWith('.js')) {
        // The literal's text without its quotes ends 1 character before the closing quote.
        const end = literal.getEnd() - 1;
        edits.push([end - 3, end]);
      }
    }
    if (edits.length === 0) continue;
    let out = text;
    for (const [from, to] of edits.sort((a, b) => b[0] - a[0])) {
      if (out.slice(from, to) !== '.js') throw new Error(`${file}: expected ".js" at ${from}`);
      out = out.slice(0, from) + out.slice(to);
    }
    changed += edits.length;
    files++;
    if (write) writeFileSync(file, out);
  }
}

for (const app of apps.length > 0 ? apps : APPS) {
  for (const file of sourceFiles(join(root, 'apps', app, 'src'))) {
    for (const literal of moduleLiterals(parse(file, readFileSync(file, 'utf8')))) {
      if (isRelative(literal.text) && literal.text.endsWith('.js')) remaining++;
    }
  }
}
console.log(
  `${write ? 'changed' : 'would change'} ${changed} specifiers in ${files} files; ${remaining} relative .js specifiers ${write ? 'left' : 'present'}`,
);
