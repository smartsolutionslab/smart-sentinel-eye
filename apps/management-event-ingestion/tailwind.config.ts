import type { Config } from 'tailwindcss';
import { tailwindTheme } from '../shared/src/ui/tokens/tailwindTheme';

// Mirrors apps/management-cameras/tailwind.config.ts: this remote compiles
// its own utilities-only build so a remote-only class change never requires
// a shell rebuild. Content is scoped to this package's own ./src, not
// ../shared/src — the shell's build already generates the shared
// primitives' classes.
const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  theme: tailwindTheme,
  plugins: [],
};

export default config;
