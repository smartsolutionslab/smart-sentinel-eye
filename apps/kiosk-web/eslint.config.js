import js from '@eslint/js';
import tseslint from '@typescript-eslint/eslint-plugin';
import tsparser from '@typescript-eslint/parser';
import reactPlugin from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import prettier from 'eslint-config-prettier';

export default [
  js.configs.recommended,
  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: {
      parser: tsparser,
      parserOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
        ecmaFeatures: { jsx: true },
      },
      globals: {
        window: 'readonly',
        document: 'readonly',
        navigator: 'readonly',
        console: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        performance: 'readonly',
        fetch: 'readonly',
        WebSocket: 'readonly',
        URL: 'readonly',
        URLSearchParams: 'readonly',
      },
    },
    plugins: {
      '@typescript-eslint': tseslint,
      react: reactPlugin,
      'react-hooks': reactHooks,
    },
    settings: { react: { version: '19' } },
    rules: {
      ...tseslint.configs.recommended.rules,
      ...reactPlugin.configs.recommended.rules,
      ...reactHooks.configs.recommended.rules,
      'react/react-in-jsx-scope': 'off',
      'react/prop-types': 'off',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  // ADR-0150 — waiting is a condition, not a count. Constitution §Testing,
  // "Waiting". A fixed count of event-loop yields is not a bound on work that
  // advances in another phase of the loop: it passes on the author's machine and
  // fails under CI contention. #2392 / spec 161.
  //
  // WHAT THIS RULE CANNOT SEE — stated, not assumed away (ADR-0150 §3):
  //   * a settle hidden inside a wrapper: `await goLive()` where goLive() settles.
  //   * a settle two or more statements before the assertion it guards.
  //   * a `waitUntil` whose condition is already true on entry, so the loop never
  //     iterates — a settle that cannot fail, wearing the sanctioned idiom's
  //     clothes. Spec 159 shipped one and review caught it. No syntactic rule
  //     will find it; reviewers keep that obligation explicitly.
  //   * anything under `e2e/` — those are `*.spec.ts`, outside ADR-0150's glob.
  // The rule is NECESSARY, NOT SUFFICIENT.
  {
    files: ['src/**/*.test.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          // Selector B — the instrument itself. A counted loop that yields to the
          // TIMER phase. Deliberately does not match `await Promise.resolve()`:
          // N microtask rounds DO bound an N-deep microtask chain, with no
          // wall-clock dependence, and those drains are load-bearing here
          // (spec.md §3.1 — five tests go red without them).
          selector: "ForStatement[test.right.type='Literal'] AwaitExpression > NewExpression[callee.name='Promise']",
          message:
            'A fixed count of timer yields is not a bound on work that advances in ' +
            'another phase of the event loop (ADR-0150). Poll the condition against ' +
            'a wall-clock deadline — waitFor / findBy* / waitUntil — or drain ' +
            'microtasks with flushMicrotasks() if that is genuinely what you need.',
        },
        {
          // Selector A — ADR-0150 §2, literally: a settle may not IMMEDIATELY
          // PRECEDE an assertion. Reserved-name guard; see the limits above.
          selector:
            "ExpressionStatement[expression.type='AwaitExpression']" +
            "[expression.argument.type='CallExpression']" +
            '[expression.argument.callee.name=/^(flushConnect|settle|pump|spin)$/]' +
            " + ExpressionStatement:has(CallExpression[callee.name='expect'])",
          message:
            'A fixed-count settle may not immediately precede an assertion ' +
            '(ADR-0150 §2). Driving fakes forward is fine; synchronising an ' +
            'assertion is not. Wait for the condition: waitFor / findBy* / waitUntil.',
        },
      ],
    },
  },
  prettier,
];
