import type { Config } from 'tailwindcss';
import { tailwindTheme } from '../shared/src/ui/tokens/tailwindTheme';

// Plan 316 §5.2: this remote compiles its own utilities-only build so a
// remote-only class change never requires a shell rebuild (US3). Content is
// scoped to this package's own ./src — not ../shared/src — because the
// shell's build already generates the shared primitives' classes; widening
// this content set would just duplicate work, not add coverage.
const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  theme: tailwindTheme,
  plugins: [],
};

export default config;
