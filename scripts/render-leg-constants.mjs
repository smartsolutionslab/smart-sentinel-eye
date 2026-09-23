// scripts/render-leg-constants.mjs
//
// Shared constants for the render-leg baseline family
// (render-leg-check.mjs, render-leg-baseline-agreement.mjs) — one place so
// the precision used to compare a stated figure against a derived one
// cannot drift between the two scripts that both need it. Phase-6 review
// (spec 225): "two places recording the same fact, read separately" is
// exactly how this repository's §II and §IV drifted before.

// baseline.json's self-check precision (plan.md §8.3) — used both by
// render-leg-check.mjs's self-verification of baseline.json against itself,
// and by render-leg-baseline-agreement.mjs's comparison of baseline.json
// against figures.md.
export const AGREEMENT_EPSILON = 0.01;
