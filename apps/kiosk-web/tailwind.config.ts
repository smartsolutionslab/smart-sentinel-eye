import type { Config } from 'tailwindcss';
import { tailwindTheme } from '../shared/src/ui/tokens/tailwindTheme';

// Tokens are CSS custom properties in apps/shared/src/ui/tokens/tokens.css
// (ADR-0078, ADR-0148). The theme object is shared with management-web so
// both apps compile the same scale from the same tokens.
const config: Config = {
  content: ['./index.html', './src/**/*.{ts,tsx}', '../shared/src/**/*.{ts,tsx}'],
  theme: tailwindTheme,
  plugins: [],
};

export default config;
