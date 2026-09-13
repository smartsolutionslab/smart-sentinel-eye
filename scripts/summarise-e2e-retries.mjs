#!/usr/bin/env node
// Stub for #2077 / spec 145 — phase 4a only.
//
// This file exists so scripts/summarise-e2e-retries.test.mjs resolves the
// module instead of failing on ERR_MODULE_NOT_FOUND, which is a missing-file
// red, not a missing-behaviour one (ADR-0139). It intentionally does nothing:
// no argument parsing, no report reading, no summary rendering. T010 (phase
// 4b) replaces this with the real implementation described in
// specs/145-a-retried-pass-says-so/plan.md.
