// Spec 469 (#3199). Proves docs/domain/<context>.md accounts for every Domain type of that context
// and that every source path it cites exists. Mentioned is not the same as described correctly, and a
// citation that resolves is not the same as one that still points at the claimed code: both stay a
// reviewer's job (plan R2).
//
//   node scripts/domain-docs-trace.mjs [--root <dir>] [--context <Name>] [--verbose]
//
// Exit 1 on any violation.
import { existsSync, readdirSync, readFileSync, realpathSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const DECLARATION =
  /^\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+(?:(?:sealed|abstract|static|readonly|partial|unsafe|ref|new)\s+)*(record\s+struct|record\s+class|record|class|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]*)/gm;
const CITED_PATH = /^(src\/[^\s`]+?\.cs)(?::(\d+))?$/;
const LONE_IDENTIFIER = /^([A-Za-z_][A-Za-z0-9_]*)(?:<[^`]*>)?\??(?:\(\))?$/;
const EMPTY_REASON = /^(tbd|todo|n\/?a|none|-+)$/i;

const kebab = (name) => name.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

function* csFiles(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin') continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) yield* csFiles(full);
    else if (entry.name.endsWith('.cs')) yield full;
  }
}

// Only comments that begin a line: a `//` or `/*` inside a string literal (e.g. "rtsp://") must not
// swallow the rest of a line or file, and a trailing comment cannot hold a line-start declaration.
function stripComments(source) {
  return source.replace(/^\s*\/\*[\s\S]*?\*\//gm, '').replace(/^\s*\/\/[^\n]*/gm, '');
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
  const entries = [];
  let level = 0;
  for (const line of markdown.split('\n')) {
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

function hasReason(reason) {
  const words = reason.split(/\s+/).filter((word) => /[A-Za-z]{2,}/.test(word));
  return !EMPTY_REASON.test(reason) && words.length >= 2;
}

function lineCount(file) {
  const lines = readFileSync(file, 'utf8').split('\n');
  return lines.at(-1) === '' ? lines.length - 1 : lines.length;
}

function citationViolations(root, page, context, spans) {
  const found = [];
  for (const span of spans) {
    if (!span.startsWith('src/') || !span.includes('.cs')) continue;
    const cited = CITED_PATH.exec(span);
    if (!cited) {
      found.push({ kind: 'malformed-citation', context, page, message: `${page} has the citation "${span}"; cite src/…/File.cs or src/…/File.cs:LINE (one line).` });
      continue;
    }
    const file = join(root, cited[1]);
    if (!existsSync(file)) {
      found.push({ kind: 'dead-path', context, page, message: `${page} cites ${cited[1]}, which does not exist.` });
    } else if (cited[2] !== undefined && Number(cited[2]) > lineCount(file)) {
      found.push({ kind: 'dead-line', context, page, message: `${page} cites ${cited[1]}:${cited[2]}, but the file has ${lineCount(file)} lines.` });
    }
  }
  return found;
}

export function traceDomainDocs({ root, context }) {
  const violations = [];
  const counts = {};
  const types = [];
  const all = readdirSync(join(root, 'src'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && existsSync(join(root, 'src', entry.name, 'Domain')))
    .map((entry) => entry.name)
    .sort();
  const contexts = all.filter((name) => !context || name === context);

  if (context && contexts.length === 0) {
    violations.push({
      kind: 'unknown-context',
      context,
      page: '',
      message: `Unknown context "${context}": the contexts with a Domain layer are ${all.join(', ')}.`,
    });
  }

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
    // A type counts as named only by a span that is that identifier (optionally generic, nullable or a
    // call): `Rule.Create` or `kind: Event` do not name `Rule` or `Event`.
    const named = new Set(spans.map((span) => LONE_IDENTIFIER.exec(span)?.[1]).filter(Boolean));
    const declaredNames = new Set(declared.map((type) => type.name));

    for (const entry of omittedEntries(page)) {
      if (!hasReason(entry.reason)) {
        violations.push({
          kind: 'omitted-without-reason',
          context: name,
          page: pagePath,
          type: entry.name,
          message: `"${entry.name}" is listed under Omitted on ${pagePath} (context ${name}) without a reason of at least two words.`,
        });
      }
      const omitted = LONE_IDENTIFIER.exec(entry.name)?.[1];
      if (!omitted || !declaredNames.has(omitted)) {
        violations.push({
          kind: 'stale-omitted',
          context: name,
          page: pagePath,
          type: entry.name,
          message: `"${entry.name}" is listed under Omitted on ${pagePath} but is not a Domain type of ${name}: remove it.`,
        });
      }
    }

    for (const type of declaredNames) {
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

    violations.push(...citationViolations(root, pagePath, name, spans));
  }

  if (!context) {
    const known = new Set(all.map(kebab));
    const pages = existsSync(join(root, 'docs/domain')) ? readdirSync(join(root, 'docs/domain')) : [];
    for (const file of pages.filter((entry) => entry.endsWith('.md') && entry !== 'README.md')) {
      if (!known.has(file.slice(0, -3))) {
        violations.push({
          kind: 'orphan-page',
          context: file.slice(0, -3),
          page: `docs/domain/${file}`,
          message: `docs/domain/${file} documents a context that has no Domain layer under src/: remove the page or restore the context.`,
        });
      }
    }
    if (existsSync(join(root, 'docs/domain/README.md'))) {
      const overview = withoutFences(readFileSync(join(root, 'docs/domain/README.md'), 'utf8'));
      violations.push(...citationViolations(root, 'docs/domain/README.md', 'overview', backtickSpans(overview)));
    }
  }

  return { violations, counts, types };
}

function main(argv) {
  const fail = (message) => {
    console.error(`FAIL usage: ${message}`);
    process.exit(1);
  };
  const option = (flag) => {
    const at = argv.indexOf(flag);
    if (at === -1) return undefined;
    const value = argv[at + 1];
    if (value === undefined || value.startsWith('--')) fail(`${flag} needs a value.`);
    return value;
  };
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

// realpath on both sides: Node resolves symlinks for import.meta.url but not for argv[1], so a repo
// reached through a symlink or junction would otherwise run nothing and exit 0.
const invoked = process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url));
if (invoked) main(process.argv.slice(2));
