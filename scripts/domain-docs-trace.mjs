// Spec 469 (#3199). Proves docs/domain/<context>.md accounts for every Domain type of that context
// and that every source path it cites exists. Mentioned is not the same as described correctly:
// that part stays a reviewer's job (plan R2).
//
//   node scripts/domain-docs-trace.mjs [--root <dir>] [--context <Name>] [--verbose]
//
// Exit 1 on any violation.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const DECLARATION =
  /^\s*(?:public|internal)\s+(?:(?:sealed|abstract|static|readonly|partial|unsafe|ref|new)\s+)*(record\s+struct|record\s+class|record|class|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]*)/gm;
const CITED_PATH = /^(src\/[^\s`]+?\.cs)(?::(\d+))?$/;

const kebab = (name) => name.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

function* csFiles(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin') continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) yield* csFiles(full);
    else if (entry.name.endsWith('.cs')) yield full;
  }
}

function stripComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
}

function domainTypes(root, context) {
  const types = [];
  for (const file of csFiles(join(root, 'src', context, 'Domain'))) {
    const source = stripComments(readFileSync(file, 'utf8'));
    for (const match of source.matchAll(DECLARATION)) {
      types.push({ context, name: match[2], kind: match[1], file: file.slice(root.length + 1).replaceAll('\\', '/') });
    }
  }
  return types;
}

function withoutFences(markdown) {
  return markdown.replace(/^```[\s\S]*?^```/gm, '');
}

function backtickSpans(markdown) {
  return [...markdown.matchAll(/`([^`\n]+)`/g)].map((match) => match[1]);
}

function omittedEntries(markdown) {
  const lines = markdown.split('\n');
  const entries = [];
  let level = 0;
  for (const line of lines) {
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      if (/^omitted\b/i.test(heading[2])) level = heading[1].length;
      else if (level > 0 && heading[1].length <= level) level = 0;
      continue;
    }
    const item = level > 0 ? /^\s*[-*]\s+`([^`]+)`(.*)$/.exec(line) : null;
    if (item) entries.push({ name: item[1], reason: item[2].replace(/^[\s—–:\-]+/, '').trim() });
  }
  return entries;
}

function lineCount(file) {
  const lines = readFileSync(file, 'utf8').split('\n');
  return lines.at(-1) === '' ? lines.length - 1 : lines.length;
}

export function traceDomainDocs({ root, context }) {
  const violations = [];
  const counts = {};
  const types = [];
  const contexts = readdirSync(join(root, 'src'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && existsSync(join(root, 'src', entry.name, 'Domain')))
    .map((entry) => entry.name)
    .filter((name) => !context || name === context)
    .sort();

  for (const name of contexts) {
    const declared = domainTypes(root, name);
    types.push(...declared);
    counts[name] = declared.length;

    const pagePath = `docs/domain/${kebab(name)}.md`;
    if (!existsSync(join(root, pagePath))) {
      violations.push({ kind: 'missing-page', context: name, page: pagePath, message: `Context ${name} has no page: expected ${pagePath}.` });
      continue;
    }

    const page = withoutFences(readFileSync(join(root, pagePath), 'utf8'));
    const spans = backtickSpans(page);
    const named = new Set(
      spans.filter((span) => !span.includes('/')).flatMap((span) => span.match(/[A-Za-z_][A-Za-z0-9_]*/g) ?? []),
    );

    for (const entry of omittedEntries(page)) {
      if (entry.reason === '') {
        violations.push({
          kind: 'omitted-without-reason',
          context: name,
          page: pagePath,
          type: entry.name,
          message: `"${entry.name}" is listed under Omitted on ${pagePath} (context ${name}) with no reason.`,
        });
      }
    }

    for (const type of [...new Set(declared.map((t) => t.name))]) {
      if (!named.has(type)) {
        violations.push({
          kind: 'unaccounted-type',
          context: name,
          page: pagePath,
          type,
          message: `Domain type "${type}" (context ${name}) is not named on ${pagePath}: name it in backticks, or list it under "## Omitted" with a reason.`,
        });
      }
    }

    for (const span of spans) {
      const cited = CITED_PATH.exec(span);
      if (!cited) continue;
      const file = join(root, cited[1]);
      if (!existsSync(file)) {
        violations.push({ kind: 'dead-path', context: name, page: pagePath, message: `${pagePath} cites ${cited[1]}, which does not exist.` });
      } else if (cited[2] !== undefined && Number(cited[2]) > lineCount(file)) {
        violations.push({
          kind: 'dead-line',
          context: name,
          page: pagePath,
          message: `${pagePath} cites ${cited[1]}:${cited[2]}, but the file has ${lineCount(file)} lines.`,
        });
      }
    }
  }

  return { violations, counts, types };
}

function main(argv) {
  const option = (flag) => (argv.includes(flag) ? argv[argv.indexOf(flag) + 1] : undefined);
  const root = resolve(option('--root') ?? join(dirname(fileURLToPath(import.meta.url)), '..'));
  const { violations, counts, types } = traceDomainDocs({ root, context: option('--context') });

  if (argv.includes('--verbose')) {
    for (const type of types) console.log(`${type.context}\t${type.kind}\t${type.name}\t${type.file}`);
  }
  for (const [name, count] of Object.entries(counts)) console.log(`${name}: ${count} Domain types`);
  for (const violation of violations) console.error(`FAIL ${violation.kind}: ${violation.message}`);
  console.log(`${violations.length} violation(s) across ${Object.keys(counts).length} context(s).`);
  process.exit(violations.length === 0 ? 0 : 1);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main(process.argv.slice(2));
