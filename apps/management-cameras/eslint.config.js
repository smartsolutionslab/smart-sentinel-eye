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
        fetch: 'readonly',
        Response: 'readonly',
        Request: 'readonly',
        Headers: 'readonly',
        WebSocket: 'readonly',
        URL: 'readonly',
        URLSearchParams: 'readonly',
        globalThis: 'readonly',
        AbortController: 'readonly',
        KeyboardEvent: 'readonly',
        HTMLElement: 'readonly',
        HTMLOptionElement: 'readonly',
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
      'no-useless-assignment': 'error',
      // FR-006 (plan §7): no cross-package relative import from the remote
      // into the shell. This is a lint gate, not a convention.
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/management-web/**'],
              message: 'apps/management-cameras must not import from apps/management-web (plan 316 §7, FR-006).',
            },
          ],
        },
      ],
    },
  },
  prettier,
];
