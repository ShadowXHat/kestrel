import type { Config } from 'tailwindcss';

const config: Config = {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        kestrel: {
          base: '#070a0f',
          panel: '#0d131f',
          surface: '#141d2e',
          'surface-hover': '#1b273d',
          border: '#1e293b',
          'border-strong': '#334155',
          text: '#f8fafc',
          muted: '#94a3b8',
          subtle: '#64748b',
          
          // Accent: Cyber Sky Cyan (strictly for interactive elements & focus)
          accent: {
            DEFAULT: '#0ea5e9',
            hover: '#38bdf8',
            active: '#0284c7',
            subtle: 'rgba(14, 165, 233, 0.15)',
            ring: '#0ea5e9',
          },

          // Severity Matrix (colorblind-accessible: distinguishable hues + lightness variation)
          severity: {
            info: {
              DEFAULT: '#38bdf8',
              bg: 'rgba(56, 189, 248, 0.12)',
              border: 'rgba(56, 189, 248, 0.3)',
              text: '#7dd3fc',
            },
            low: {
              DEFAULT: '#2dd4bf',
              bg: 'rgba(45, 212, 191, 0.12)',
              border: 'rgba(45, 212, 191, 0.3)',
              text: '#5eead4',
            },
            medium: {
              DEFAULT: '#fbbf24',
              bg: 'rgba(251, 191, 36, 0.12)',
              border: 'rgba(251, 191, 36, 0.3)',
              text: '#fde047',
            },
            high: {
              DEFAULT: '#f97316',
              bg: 'rgba(249, 115, 22, 0.12)',
              border: 'rgba(249, 115, 22, 0.3)',
              text: '#fdba74',
            },
            critical: {
              DEFAULT: '#f43f5e',
              bg: 'rgba(244, 63, 94, 0.15)',
              border: 'rgba(244, 63, 94, 0.35)',
              text: '#fda4af',
            },
          },

          // Role Badges
          role: {
            viewer: {
              DEFAULT: '#94a3b8',
              bg: 'rgba(148, 163, 184, 0.12)',
              border: 'rgba(148, 163, 184, 0.3)',
            },
            analyst: {
              DEFAULT: '#38bdf8',
              bg: 'rgba(56, 189, 248, 0.12)',
              border: 'rgba(56, 189, 248, 0.3)',
            },
            admin: {
              DEFAULT: '#c084fc',
              bg: 'rgba(192, 132, 252, 0.12)',
              border: 'rgba(192, 132, 252, 0.3)',
            },
          },
        },
      },
      fontFamily: {
        sans: ['"Plus Jakarta Sans"', 'Inter', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'monospace'],
      },
      boxShadow: {
        glow: '0 0 16px -3px rgba(14, 165, 233, 0.25)',
        'glow-danger': '0 0 16px -3px rgba(244, 63, 94, 0.25)',
      },
    },
  },
  plugins: [],
};

export default config;
