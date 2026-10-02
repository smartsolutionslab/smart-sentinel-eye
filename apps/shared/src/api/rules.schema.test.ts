import { describe, it, expect } from 'vitest';
import { createRuleSchema } from './rules.schema.js';

/**
 * Spec 296 (#2618) US2. `createRuleSchema` has no tests today (confirmed on
 * `develop@27e68e11`), so there is nothing "existing" to preserve for the new
 * `SwitchWallScene` cases below — they are new-behaviour, RED (ADR-0139/0144):
 * today's `superRefine` is a two-way `if (SetVariableValue) … else …`, and a
 * `SwitchWallScene` value falls into the `else` branch, so it is validated as
 * though it were a `HighlightOverlay` rule (requiring `overlayIdentifier` and
 * `durationMs`, not `wallIdentifier`/`sceneTarget` at all). Mirrors the
 * server's `RuleAction.SwitchWallScene.From` (plan.md §4.1): a missing
 * wall/target, a target other than `'Next'`/`'Layout'`, `Layout` without a
 * layout, and `Next` *with* one are all refused client-side the same way the
 * server's `400 RULE_INVALID_INPUT` already refuses them.
 *
 * The `SetVariableValue`/`HighlightOverlay` cases in the second `describe`
 * below are deliberately NOT new-behaviour: they characterise the two-way
 * `superRefine` as it stands *today*, so they are observed GREEN now (T119)
 * and must stay green and unmodified once T124 rewrites it into a three-way
 * switch — the safety net that the rewrite does not silently change what the
 * two existing action types require.
 */

function baseRule(overrides: Record<string, unknown> = {}) {
  return {
    name: 'switch-line-3',
    triggerSource: 'manual',
    triggerKind: 'LineStop',
    predicate: '$.payload.line == 3',
    ...overrides,
  };
}

/**
 * The dotted paths of every issue a `safeParse` result carries, or `[]` on
 * success. Asserting on the *specific* field a "required"/"rejected" case
 * names — not merely `result.success === false` — matters here: today,
 * `'SwitchWallScene'` alone already fails the top-level `actionType` enum
 * (the two existing literals only), so a bare `success === false` would be
 * green on every "requires X"/"rejects Y" case below for the wrong reason —
 * the enum rejection, not the behaviour the case names. Checking the path
 * keeps each case red until the field it names is actually validated.
 */
function issuePaths(result: ReturnType<typeof createRuleSchema.safeParse>): string[] {
  return result.success ? [] : result.error.issues.map((issue) => issue.path.join('.'));
}

describe('createRuleSchema — SwitchWallScene (spec 296 US2, new behaviour, RED)', () => {
  it('Accepts a SwitchWallScene action targeting a specific layout', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'Layout',
        targetLayoutIdentifier: '019f0000-0000-7000-8000-000000000002',
      }),
    );

    expect(result.success).toBe(true);
  });

  it('Accepts a SwitchWallScene action targeting Next with no layout', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'Next',
      }),
    );

    expect(result.success).toBe(true);
  });

  it('Requires wallIdentifier for SwitchWallScene', () => {
    const result = createRuleSchema.safeParse(baseRule({ actionType: 'SwitchWallScene', sceneTarget: 'Next' }));

    expect(issuePaths(result)).toContain('wallIdentifier');
  });

  it('Requires sceneTarget for SwitchWallScene', () => {
    const result = createRuleSchema.safeParse(
      baseRule({ actionType: 'SwitchWallScene', wallIdentifier: '019f0000-0000-7000-8000-000000000001' }),
    );

    expect(issuePaths(result)).toContain('sceneTarget');
  });

  it('Rejects a sceneTarget outside Next/Layout (lowercase "next")', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'next',
      }),
    );

    expect(issuePaths(result)).toContain('sceneTarget');
  });

  it('Rejects a sceneTarget outside Next/Layout (an unrelated string)', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'Previous',
      }),
    );

    expect(issuePaths(result)).toContain('sceneTarget');
  });

  it('Requires targetLayoutIdentifier when sceneTarget is Layout', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'Layout',
      }),
    );

    expect(issuePaths(result)).toContain('targetLayoutIdentifier');
  });

  it('Forbids targetLayoutIdentifier when sceneTarget is Next (the server 400s it)', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'SwitchWallScene',
        wallIdentifier: '019f0000-0000-7000-8000-000000000001',
        sceneTarget: 'Next',
        targetLayoutIdentifier: '019f0000-0000-7000-8000-000000000002',
      }),
    );

    expect(issuePaths(result)).toContain('targetLayoutIdentifier');
  });
});

describe('createRuleSchema — the two existing action types (characterisation, observed GREEN today, stays unmodified through T124)', () => {
  it('Requires variableName and valueExpression for SetVariableValue', () => {
    const result = createRuleSchema.safeParse(baseRule({ actionType: 'SetVariableValue' }));

    expect(result.success).toBe(false);
  });

  it('Accepts a complete SetVariableValue rule', () => {
    const result = createRuleSchema.safeParse(
      baseRule({ actionType: 'SetVariableValue', variableName: 'oeeLine1', valueExpression: '42' }),
    );

    expect(result.success).toBe(true);
  });

  it('Requires overlayIdentifier and durationMs for HighlightOverlay', () => {
    const result = createRuleSchema.safeParse(baseRule({ actionType: 'HighlightOverlay' }));

    expect(result.success).toBe(false);
  });

  it('Accepts a complete HighlightOverlay rule', () => {
    const result = createRuleSchema.safeParse(
      baseRule({
        actionType: 'HighlightOverlay',
        overlayIdentifier: '123e4567-e89b-12d3-a456-426614174000',
        durationMs: 5000,
      }),
    );

    expect(result.success).toBe(true);
  });
});
