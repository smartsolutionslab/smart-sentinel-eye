import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';
import '@smart-sentinel-eye/shared/test/radixJsdom';

// Mirrors apps/management-cameras/src/test/setup.ts. ADR-0150 §1: the
// deadline is a FAILURE BOUND, not a wait. 10_000 matches the shell's own
// setup and apps/shared's workspace-level default.
configure({ asyncUtilTimeout: 10_000 });
