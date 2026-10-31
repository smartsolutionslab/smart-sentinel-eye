import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { traceDomainDocs } from './domain-docs-trace.mjs';

// Spec 469 (#3199) FR-004/FR-007, plan R1/R2. The guard proves every Domain type of a
// context is NAMED on its page (or listed under Omitted with a reason) and that every
// cited source path resolves. It cannot prove a description is right: only that it exists.

const roots = [];

afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

function tree(files) {
  const root = mkdtempSync(join(tmpdir(), 'domain-docs-'));
  roots.push(root);
  for (const [path, content] of Object.entries(files)) {
    const full = join(root, path);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}

const WALL = 'namespace Ctx;\npublic sealed class Wall : AggregateRoot\n{\n}\n';
const WALL_NAME = 'namespace Ctx;\npublic readonly record struct WallName(string Value);\n';

const CLEAN = {
  'src/Shared/Domain/Wall.cs': WALL,
  'src/Shared/Domain/WallName.cs': WALL_NAME,
  'docs/domain/shared.md': '# Shared\n\n`Wall` is the root, see `src/Shared/Domain/Wall.cs:3`.\n\n`WallName` wraps a string.\n',
};

describe('the repository', () => {
  it('has a domain page for every Domain type and every citation on it resolves', () => {
    const root = join(dirname(fileURLToPath(import.meta.url)), '..');

    const { violations } = traceDomainDocs({ root });

    // This test reads the live tree: it fails when a Domain type and its page disagree, not when
    // scripts/ is broken. The fix is in docs/domain/<context>.md (or the type), per the message.
    assert.deepEqual(
      violations.map((violation) => violation.message),
      [],
    );
  });
});

describe('traceDomainDocs', () => {
  it('passes when every type is named and every citation resolves', () => {
    const { violations, counts } = traceDomainDocs({ root: tree(CLEAN) });

    assert.deepEqual(violations, []);
    assert.equal(counts.Shared, 2);
  });

  it('names the type, the context and the page when a type is missing from the page', () => {
    const files = { ...CLEAN, 'docs/domain/shared.md': '# Shared\n\n`Wall` only.\n' };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'unaccounted-type');
    assert.match(violations[0].message, /WallName/);
    assert.match(violations[0].message, /Shared/);
    assert.match(violations[0].message, /docs\/domain\/shared\.md/);
  });

  it('fails when a context has no page at all', () => {
    const files = { 'src/Shared/Domain/Wall.cs': WALL };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'missing-page');
  });

  it('accepts a type listed under Omitted with a reason', () => {
    const files = {
      ...CLEAN,
      'docs/domain/shared.md': '# Shared\n\n`Wall` only.\n\n## Omitted\n\n- `WallName` — a plain wrapper, no rule\n',
    };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.deepEqual(violations, []);
  });

  it('rejects an Omitted entry that gives no reason', () => {
    const files = {
      ...CLEAN,
      'docs/domain/shared.md': '# Shared\n\n`Wall` only.\n\n## Omitted\n\n- `WallName`\n',
    };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'omitted-without-reason');
    assert.match(violations[0].message, /WallName/);
  });

  it('rejects a cited path that does not exist', () => {
    const files = { ...CLEAN, 'docs/domain/shared.md': CLEAN['docs/domain/shared.md'] + '\nSee `src/Shared/Domain/Gone.cs`.\n' };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'dead-path');
    assert.match(violations[0].message, /Gone\.cs/);
  });

  it('rejects a cited line past the end of the file', () => {
    const files = { ...CLEAN, 'docs/domain/shared.md': CLEAN['docs/domain/shared.md'] + '\nSee `src/Shared/Domain/Wall.cs:99`.\n' };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'dead-line');
  });

  it('does not count a type that is only named inside a fenced code block', () => {
    const files = {
      ...CLEAN,
      'docs/domain/shared.md': '# Shared\n\n`Wall` only.\n\n```text\n`WallName`\n```\n',
    };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.equal(violations.length, 1);
    assert.equal(violations[0].kind, 'unaccounted-type');
    assert.match(violations[0].message, /WallName/);
  });

  it('ignores types under obj/ and bin/', () => {
    const files = {
      ...CLEAN,
      'src/Shared/Domain/obj/Generated.cs': 'public class Generated {}\n',
      'src/Shared/Domain/bin/Other.cs': 'public class Other {}\n',
    };

    const { violations, counts } = traceDomainDocs({ root: tree(files) });

    assert.deepEqual(violations, []);
    assert.equal(counts.Shared, 2);
  });

  it('ignores non-public types and the members of a type', () => {
    const files = {
      ...CLEAN,
      'src/Shared/Domain/Helper.cs': 'namespace Ctx;\nfile class Helper {}\nprivate sealed class Nested {}\n',
    };

    const { counts } = traceDomainDocs({ root: tree(files) });

    assert.equal(counts.Shared, 2);
  });

  it('kebab-cases the context name for the page path', () => {
    const files = {
      'src/AuditObservability/Domain/Chunk.cs': 'public sealed class Chunk {}\n',
      'docs/domain/audit-observability.md': '# Audit\n\n`Chunk`\n',
    };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.deepEqual(violations, []);
  });

  it('also checks the citations in the overview page', () => {
    const files = {
      ...CLEAN,
      'docs/domain/README.md': '# Domain\n\nOK `src/Shared/Domain/Wall.cs:3`, gone `src/Shared/Domain/Gone.cs:1`, far `src/Shared/Domain/Wall.cs:99`.\n',
    };

    const { violations } = traceDomainDocs({ root: tree(files) });

    assert.deepEqual(violations.map((violation) => violation.kind).sort(), ['dead-line', 'dead-path']);
    assert.ok(violations.every((violation) => violation.page === 'docs/domain/README.md'));
  });

  it('does not demand the overview when it is absent', () => {
    const { violations } = traceDomainDocs({ root: tree(CLEAN) });

    assert.deepEqual(violations, []);
  });

  it('skips the overview when the run is limited to one context', () => {
    const files = { ...CLEAN, 'docs/domain/README.md': '# Domain\n\n`src/Shared/Domain/Gone.cs`\n' };

    const { violations } = traceDomainDocs({ root: tree(files), context: 'Shared' });

    assert.deepEqual(violations, []);
  });

  it('limits the run to one context when asked', () => {
    const files = {
      ...CLEAN,
      'src/Other/Domain/Thing.cs': 'public sealed class Thing {}\n',
    };

    const { violations, counts } = traceDomainDocs({ root: tree(files), context: 'Shared' });

    assert.deepEqual(violations, []);
    assert.deepEqual(Object.keys(counts), ['Shared']);
  });
});
