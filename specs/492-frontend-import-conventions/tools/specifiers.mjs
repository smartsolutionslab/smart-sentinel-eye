import { createRequire } from 'node:module';
import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const require = createRequire(new URL('../../../apps/shared/package.json', import.meta.url));
export const ts = require('typescript');

export const APPS = ['shared', 'kiosk-web', 'management-cameras', 'management-event-ingestion', 'management-web'];

const MOCKS = new Set(['mock', 'doMock', 'importActual', 'importMock']);

/** Every string literal that names a module: static, re-export, side-effect, import(), import type, and the vi.mock family. */
export function moduleLiterals(sourceFile) {
  const found = [];
  const visit = (node) => {
    if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier) {
      found.push(node.moduleSpecifier);
    } else if (ts.isCallExpression(node)) {
      const [first] = node.arguments;
      const isDynamic = node.expression.kind === ts.SyntaxKind.ImportKeyword;
      const callee = node.expression;
      const isMock =
        ts.isPropertyAccessExpression(callee) &&
        ts.isIdentifier(callee.expression) &&
        callee.expression.text === 'vi' &&
        MOCKS.has(callee.name.text);
      if ((isDynamic || isMock) && first && ts.isStringLiteralLike(first)) found.push(first);
    } else if (
      ts.isImportTypeNode(node) &&
      ts.isLiteralTypeNode(node.argument) &&
      ts.isStringLiteralLike(node.argument.literal)
    ) {
      found.push(node.argument.literal);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return found;
}

export const isRelative = (text) => text.startsWith('./') || text.startsWith('../');

export function sourceFiles(dir) {
  const out = [];
  const walk = (d) => {
    for (const name of readdirSync(d)) {
      if (name === 'node_modules' || name === 'dist') continue;
      const p = join(d, name);
      if (statSync(p).isDirectory()) walk(p);
      else if (/\.(ts|tsx)$/.test(name) && !name.endsWith('.d.ts')) out.push(p);
    }
  };
  walk(dir);
  return out.sort();
}

export const parse = (file, text) =>
  ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.ES2023,
    true,
    file.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
  );
