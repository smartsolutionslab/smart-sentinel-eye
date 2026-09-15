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
        HTMLElement: 'readonly',
        HTMLButtonElement: 'readonly',
        HTMLInputElement: 'readonly',
        HTMLDivElement: 'readonly',
        HTMLFormElement: 'readonly',
        HTMLVideoElement: 'readonly',
        MediaStream: 'readonly',
        RTCPeerConnection: 'readonly',
        RTCPeerConnectionState: 'readonly',
        RTCSessionDescriptionInit: 'readonly',
        RTCStatsReport: 'readonly',
        performance: 'readonly',
        requestAnimationFrame: 'readonly',
        AbortController: 'readonly',
        AbortSignal: 'readonly',
        Response: 'readonly',
        Request: 'readonly',
        RequestInfo: 'readonly',
        Node: 'readonly',
        RequestInit: 'readonly',
        WebSocket: 'readonly',
        fetch: 'readonly',
        URL: 'readonly',
        console: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        globalThis: 'readonly',
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
  //   * Selector A's name list is CLOSED: `flushConnect` / `settle` / `pump` /
  //     `spin` only. Rename the helper (`waitABit`, say) and the adjacency
  //     check cannot see it — it is a reserved-name guard, not a bound
  //     (ADR-0150 §2 amended).
  //   * Selector B's bound, after widening (#2392 phase 6 finding 2), is "has
  //     a `test` expression at all" (`ForStatement[test]`) — not "a literal
  //     bound". It still cannot reach a loop with NO `test` node: `waitUntil`
  //     is a `WhileStatement`, and a genuine `for (;;) { … break … }` poll has
  //     no `test` to match.
  //   * Selector B also covers `ForOfStatement` / `ForInStatement` now
  //     (#2392 phase 6 finding 5, measured population zero on develop and
  //     the tip before this was widened) — a fixed-count `for (const x of
  //     fixedArray) { await new Promise(...) }` is the same instrument by
  //     another shape, and a sibling suite already reaches for a `for...of`
  //     over a fixed array (`CameraViewer.test.tsx`'s retry-delay loop) even
  //     though that one drives fake timers, not a raw `new Promise`.
  //   * Selector B cannot tell a TIMER promise from an EVENT promise — `await
  //     new Promise((r) => { img.onload = r; })` in a loop is caught the same
  //     as a `setTimeout` yield, including through a nested function
  //     declared inside the loop body (the descendant combinator reaches
  //     through it). That is by design, not a bug to narrow away (#2392
  //     phase 6 finding 6): a false POSITIVE here costs one documented
  //     `eslint-disable-next-line`; narrowing to require `setTimeout` /
  //     `setInterval` evidence would just as easily be evaded by
  //     `requestAnimationFrame` or a hand-rolled `sleep()`, trading a known,
  //     cheap failure mode for an equally real one that is merely harder to
  //     see (ADR-0150's own Consequences: "a false positive is cheap; a
  //     false negative costs a day").
  // The rule is NECESSARY, NOT SUFFICIENT.
  //
  // ESCAPE HATCH: Selector B cannot tell DRIVING a fake forward from
  // SYNCHRONISING an assertion — a literal- or named-bound loop that samples
  // N frames (`for (let i = 0; i < 3; i += 1) { await new Promise((r) =>
  // requestAnimationFrame(r)); }`), or a loop awaiting an EVENT promise
  // rather than a timer (`for (const camera of cameras) { await new
  // Promise((r) => { img.onload = r; }); }`), is banned by shape even though
  // ADR-0150 permits driving. Where that is genuinely the need, use
  // `// eslint-disable-next-line no-restricted-syntax` WITH A STATED REASON —
  // do not delete the rule instead.
  {
    files: ['src/**/*.test.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          // Selector B — the instrument itself. A counted loop that yields to
          // the TIMER phase, on ANY bound — literal or named — over a
          // `for`, `for...of`, or `for...in`. Widened twice during review
          // (#2392 phase 6): finding 2 dropped the literal-only bound
          // requirement (`const ROUNDS = 10; for (let i = 0; i < ROUNDS; i +=
          // 1)` was invisible to the original `[test.right.type='Literal']`
          // selector); finding 5 added `ForOfStatement` / `ForInStatement`,
          // which have no `test` node at all and were invisible to
          // `ForStatement[test]` alone. `ForStatement[test]` on its own arm
          // still requires a `test` expression to exist, so `for (;;)` and
          // `while` stay outside it structurally. Deliberately does not
          // match `await Promise.resolve()`: N microtask rounds DO bound an
          // N-deep microtask chain, with no wall-clock dependence, and those
          // drains are load-bearing here (spec.md §3.1 — five tests go red
          // without them).
          selector:
            "ForStatement[test] AwaitExpression > NewExpression[callee.name='Promise'], " +
            "ForOfStatement AwaitExpression > NewExpression[callee.name='Promise'], " +
            "ForInStatement AwaitExpression > NewExpression[callee.name='Promise']",
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
