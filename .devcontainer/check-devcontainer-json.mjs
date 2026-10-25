// Strict syntax check for .devcontainer/devcontainer.json (spec 461, #3151).
//
// `npx @devcontainers/cli build` parses the file leniently and exits 0 on a
// missing comma. This accepts JSONC (comments, trailing commas) and rejects
// every other JSON syntax error with a line:column. No dependency.

import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

function lineColumn(text, offset) {
  const before = text.slice(0, offset);
  const line = before.split('\n').length;
  return { line, column: offset - (before.lastIndexOf('\n') + 1) + 1 };
}

// Comments become spaces (newlines kept) and trailing commas become a space, so
// offsets in the result equal offsets in the input.
function normalise(text) {
  const out = [...text];
  let state = 'code';
  let commaAt = -1;
  let commentStart = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const next = text[i + 1];
    if (state === 'string') {
      if (c === '\\') {
        i++;
      } else if (c === '"') {
        state = 'code';
      }
    } else if (state === 'line') {
      if (c === '\n') {
        state = 'code';
      } else {
        out[i] = ' ';
      }
    } else if (state === 'block') {
      if (c === '*' && next === '/') {
        out[i] = ' ';
        out[i + 1] = ' ';
        i++;
        state = 'code';
      } else if (c !== '\n') {
        out[i] = ' ';
      }
    } else if (c === '"') {
      state = 'string';
      commaAt = -1;
    } else if (c === '/' && next === '/') {
      state = 'line';
      out[i] = ' ';
      out[i + 1] = ' ';
      i++;
    } else if (c === '/' && next === '*') {
      state = 'block';
      commentStart = i;
      out[i] = ' ';
      out[i + 1] = ' ';
      i++;
    } else if (c === ',') {
      commaAt = i;
    } else if (c === '}' || c === ']') {
      if (commaAt >= 0) {
        out[commaAt] = ' ';
      }
      commaAt = -1;
    } else if (!/\s/.test(c)) {
      commaAt = -1;
    }
  }
  if (state === 'block') {
    return { ok: false, reason: 'unterminated block comment', ...lineColumn(text, commentStart) };
  }
  return { ok: true, text: out.join('') };
}

export function parseJsonc(text) {
  const normalised = normalise(text);
  if (!normalised.ok) {
    return normalised;
  }
  let value;
  try {
    value = JSON.parse(normalised.text);
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    const position = /position (\d+)/.exec(reason);
    if (position) {
      return { ok: false, reason, ...lineColumn(text, Number(position[1])) };
    }
    const atLine = /line (\d+) column (\d+)/.exec(reason);
    if (atLine) {
      return { ok: false, reason, line: Number(atLine[1]), column: Number(atLine[2]) };
    }
    return { ok: false, reason };
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return { ok: false, reason: 'the root must be a JSON object', line: 1, column: 1 };
  }
  return { ok: true, value };
}

function main() {
  const file = process.argv[2] ?? fileURLToPath(new URL('./devcontainer.json', import.meta.url));
  let text;
  try {
    text = readFileSync(file, 'utf8');
  } catch (error) {
    console.error(`${file}: cannot read: ${error.message}`);
    process.exit(1);
  }
  const result = parseJsonc(text);
  if (!result.ok) {
    const where = result.line === undefined ? '' : `:${result.line}:${result.column}`;
    console.error(`${file}${where}: ${result.reason}`);
    process.exit(1);
  }
}

const isMainModule = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;

if (isMainModule) {
  main();
}
