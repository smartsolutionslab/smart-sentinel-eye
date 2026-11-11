// Import and export conventions for every frontend app's `src` (spec 492). Spread `frontendImportRules` into an app's flat config.
//
// A later flat-config block that sets `no-restricted-syntax` or `no-restricted-imports` REPLACES this one's options for the files it
// matches. So a block that sets either must spread `RESTRICTED_SYNTAX` / `NO_EXTENSION_PATTERN` into its own list.
//
// What these rules cannot see: a specifier assembled at runtime (`import(`./${name}.js`)`), and any file `eslint src` does not lint,
// which includes the root-level configs. A barrel added to the glob below and to `exports` together is a decision, not an accident.

const EXTENSION = '^\\.{1,2}/.*\\.(js|jsx|ts|tsx)$';
const EXTENSION_MESSAGE =
  'A relative import names the module, not an emitted file: drop the extension (`./Button`, not `./Button.js`).';

export const NO_EXTENSION_PATTERN = { regex: EXTENSION, message: EXTENSION_MESSAGE };

// esquery regexes: escape the slash, no {n,m} repetition.
const EXTENSION_LITERAL = String.raw`[value=/^\.\.?\/.*\.(js|jsx|ts|tsx)$/]`;

const NO_EXTENSION_SELECTORS = [
  { selector: `ImportExpression > Literal${EXTENSION_LITERAL}`, message: EXTENSION_MESSAGE },
  {
    selector:
      "CallExpression[callee.object.name='vi'][callee.property.name=/^(mock|doMock|importActual|importMock)$/]" +
      ` > Literal.arguments:first-child${EXTENSION_LITERAL}`,
    message: EXTENSION_MESSAGE,
  },
];

const BARREL_MESSAGE =
  'Only a package public entry may re-export (a file named in apps/shared/package.json `exports`). ' +
  'Import from the module that defines it instead.';

const BARREL_SELECTORS = [
  { selector: 'ExportAllDeclaration', message: BARREL_MESSAGE },
  { selector: 'ExportNamedDeclaration[source]', message: BARREL_MESSAGE },
];

export const RESTRICTED_SYNTAX = [...NO_EXTENSION_SELECTORS, ...BARREL_SELECTORS];

// The three default exports in `src` that a tool requires: the shell reads `module.default` of each federated surface, and the
// federation plugin calls the runtime plugin's default export as a factory. Config files (vite, vitest, tailwind) are outside `src`.
const TOOL_REQUIRED_DEFAULT_EXPORTS = [
  'src/CamerasSurface.tsx',
  'src/EventIngestionSurface.tsx',
  'src/app/navigation/mismatchRuntimePlugin.ts',
];

// apps/shared's public entries that re-export (each is an `exports` target): six barrels, and CameraViewer, whose overlay types and
// elements are part of the surface other apps import and mock by that path. `ui/primitives/index.ts` re-exports nothing.
const BARREL_ENTRIES = [
  'src/{index,api/index,hooks/index,streaming/index,types/index,ui/index}.ts',
  'src/ui/composites/CameraViewer.tsx',
];

export const frontendImportRules = [
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', { patterns: [NO_EXTENSION_PATTERN] }],
      'no-restricted-syntax': ['error', ...RESTRICTED_SYNTAX],
      'no-restricted-exports': [
        'error',
        { restrictDefaultExports: { direct: true, named: true, defaultFrom: true, namedFrom: true } },
      ],
    },
  },
  { files: TOOL_REQUIRED_DEFAULT_EXPORTS, rules: { 'no-restricted-exports': 'off' } },
  { files: BARREL_ENTRIES, rules: { 'no-restricted-syntax': ['error', ...NO_EXTENSION_SELECTORS] } },
];
