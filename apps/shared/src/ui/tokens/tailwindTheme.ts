// The one Tailwind theme object both apps' tailwind.config.ts import
// (ADR-0078, ADR-0148 — spec 257, issue #2332). Cites semantic tokens from
// ./tokens.css only — never a primitive (plan.md §4.1). `colors` and
// `spacing` sit under `extend` because Tailwind's own colour/size scales
// stay available alongside ours; every other namespace here REPLACES
// Tailwind's stock scale (plan.md §4 finding F2): a config value naming
// `sm`/`md`/`lg`/... without `extend` closes off names it does not list
// (`shadow-xl`, `rounded-3xl`, `text-7xl`, `font-bold` all stop compiling).
//
// `transitionDuration`/`transitionTimingFunction` deliberately have no
// `DEFAULT` key (plan.md §4 finding F5): a `DEFAULT` there silently deletes
// Tailwind's `--default-transition-duration`/`--default-transition-timing-
// function` variables, which every stock `transition-colors` etc. resolves —
// so every transition would become instant. The two are bound instead
// through the named bridge variables tokens.css declares.
//
// No `tailwindcss` import here: this module has no build-time dependency on
// Tailwind, only on the CSS custom properties tokens.css declares. Each
// app's tailwind.config.ts assigns it to `theme` and type-checks it there.
export const tailwindTheme = {
  extend: {
    colors: {
      bg: {
        base: 'var(--color-bg-base)',
        elevated: 'var(--color-bg-elevated)',
        raised: 'var(--color-bg-raised)',
        video: 'var(--color-bg-video)',
      },
      fg: {
        primary: 'var(--color-fg-primary)',
        muted: 'var(--color-fg-muted)',
        disabled: 'var(--color-fg-disabled)',
        'on-accent': 'var(--color-fg-on-accent)',
      },
      border: {
        subtle: 'var(--color-border-subtle)',
        strong: 'var(--color-border-strong)',
      },
      accent: {
        DEFAULT: 'var(--color-accent)',
        hover: 'var(--color-accent-hover)',
        pressed: 'var(--color-accent-pressed)',
        disabled: 'var(--color-accent-disabled)',
        subtle: 'var(--color-accent-subtle)',
        active: 'var(--color-accent-active)',
        fault: 'var(--color-accent-fault)',
        warning: 'var(--color-accent-warning)',
      },
      focus: {
        ring: 'var(--color-focus-ring)',
      },
      scrim: 'var(--color-scrim)',
    },
    spacing: {
      '0.5': 'var(--space-0-5)',
      '1': 'var(--space-1)',
      '2': 'var(--space-2)',
      '3': 'var(--space-3)',
      '4': 'var(--space-4)',
      '5': 'var(--space-5)',
      '6': 'var(--space-6)',
      '8': 'var(--space-8)',
      '10': 'var(--space-10)',
      '12': 'var(--space-12)',
      '16': 'var(--space-16)',
    },
  },
  fontFamily: {
    sans: 'var(--font-sans)',
    mono: 'var(--font-mono)',
  },
  fontSize: {
    xs: ['var(--text-xs)', { lineHeight: 'var(--text-xs-leading)' }],
    sm: ['var(--text-sm)', { lineHeight: 'var(--text-sm-leading)' }],
    base: ['var(--text-base)', { lineHeight: 'var(--text-base-leading)' }],
    lg: ['var(--text-lg)', { lineHeight: 'var(--text-lg-leading)' }],
    xl: ['var(--text-xl)', { lineHeight: 'var(--text-xl-leading)' }],
    '2xl': ['var(--text-2xl)', { lineHeight: 'var(--text-2xl-leading)', letterSpacing: 'var(--tracking-tight)' }],
    '3xl': ['var(--text-3xl)', { lineHeight: 'var(--text-3xl-leading)', letterSpacing: 'var(--tracking-tight)' }],
  },
  fontWeight: {
    normal: 'var(--font-weight-regular)',
    medium: 'var(--font-weight-medium)',
    semibold: 'var(--font-weight-semibold)',
  },
  letterSpacing: {
    tight: 'var(--tracking-tight)',
    normal: 'var(--tracking-normal)',
    wide: 'var(--tracking-wide)',
  },
  borderRadius: {
    DEFAULT: 'var(--radius-md)',
    sm: 'var(--radius-sm)',
    md: 'var(--radius-md)',
    lg: 'var(--radius-lg)',
    full: 'var(--radius-full)',
  },
  boxShadow: {
    none: 'none',
    popover: 'var(--shadow-popover)',
    overlay: 'var(--shadow-overlay)',
  },
  transitionDuration: {
    fast: 'var(--duration-fast)',
    moderate: 'var(--duration-moderate)',
    slow: 'var(--duration-slow)',
  },
  transitionTimingFunction: {
    out: 'var(--ease-out)',
    in: 'var(--ease-in)',
    'in-out': 'var(--ease-in-out)',
  },
  zIndex: {
    sticky: 'var(--z-sticky)',
    overlay: 'var(--z-overlay)',
    popover: 'var(--z-popover)',
    tooltip: 'var(--z-tooltip)',
  },
} as const;
