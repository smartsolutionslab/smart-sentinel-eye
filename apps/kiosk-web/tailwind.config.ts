import type { Config } from 'tailwindcss';

const config: Config = {
  content: ['./index.html', './src/**/*.{ts,tsx}', '../shared/src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        bg: {
          base: 'var(--color-bg-base)',
          elevated: 'var(--color-bg-elevated)',
        },
        fg: {
          primary: 'var(--color-fg-primary)',
          muted: 'var(--color-fg-muted)',
        },
        accent: {
          active: 'var(--color-accent-active)',
          fault: 'var(--color-accent-fault)',
          warning: 'var(--color-accent-warning)',
        },
      },
    },
  },
  plugins: [],
};

export default config;
