// Spec 461 (closes #3151) — a broken .devcontainer/devcontainer.json fails validation.
//
// `npx @devcontainers/cli build` parses devcontainer.json leniently and exits 0
// on a missing comma (spec 456 T002). `.devcontainer/check-devcontainer-json.mjs`
// is the strict check run before it: JSONC (comments + trailing commas) is
// accepted, every other JSON syntax error is rejected with a line:column.
//
// V8's exact error wording is deliberately not pinned (plan decision 3).

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { parseJsonc } from '../.devcontainer/check-devcontainer-json.mjs';

const script = fileURLToPath(new URL('../.devcontainer/check-devcontainer-json.mjs', import.meta.url));
const realFile = fileURLToPath(new URL('../.devcontainer/devcontainer.json', import.meta.url));

test('the real devcontainer.json parses to an object', () => {
  const result = parseJsonc(readFileSync(realFile, 'utf8'));
  assert.equal(result.ok, true);
  assert.equal(typeof result.value, 'object');
  assert.notEqual(result.value, null);
  assert.equal(Array.isArray(result.value), false);
});

test('comments, trailing commas and comment-like text inside strings are accepted with the expected value', () => {
  const text = `// leading comment
{
  /* block comment */
  "url": "https://example.com/a", // trailing comment
  "glob": "/* not a comment */",
  "list": [1, 2,],
  "nested": { "a": "x,}", },
}
`;
  const result = parseJsonc(text);
  assert.equal(result.ok, true);
  assert.deepEqual(result.value, {
    url: 'https://example.com/a',
    glob: '/* not a comment */',
    list: [1, 2],
    nested: { a: 'x,}' },
  });
});

const rejected = {
  'a missing comma between members': '{\n  "a": 1\n  "b": 2\n}\n',
  'an unclosed brace': '{\n  "a": 1\n',
  'an unterminated block comment': '{\n  "a": 1 /* never closed\n}\n',
  'empty input': '',
  'a non-object root (array)': '[1, 2]',
  'a non-object root (string)': '"devcontainer"',
};

for (const [name, text] of Object.entries(rejected)) {
  test(`${name} is rejected`, () => {
    const result = parseJsonc(text);
    assert.equal(result.ok, false);
    assert.equal(typeof result.reason, 'string');
    assert.notEqual(result.reason.length, 0);
  });
}

test('a missing comma is reported with the line it is on', () => {
  const result = parseJsonc('{\n  "a": 1\n  "b": 2\n}\n');
  assert.equal(result.ok, false);
  assert.equal(result.line, 3);
});

test('the CLI exits 0 on the real devcontainer.json', () => {
  execFileSync(process.execPath, [script, realFile], { stdio: 'pipe' });
});

test('the CLI exits 1 naming the file for a missing comma', () => {
  const dir = mkdtempSync(join(tmpdir(), 'devcontainer-461-'));
  const file = join(dir, 'devcontainer.json');
  writeFileSync(file, '{\n  "a": 1\n  "b": 2\n}\n');
  assert.throws(
    () => execFileSync(process.execPath, [script, file], { stdio: 'pipe' }),
    (error) => error.status === 1 && `${error.stderr}${error.stdout}`.includes('devcontainer.json'),
  );
});
