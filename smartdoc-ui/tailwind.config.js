/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        surface: {
          0: '#090d13',
          1: '#0f1520',
          2: '#161e2e',
          3: '#1e2a3a',
          4: '#263347',
        },
        accent: {
          blue:   '#4d9eff',
          green:  '#2dba6e',
          yellow: '#e6a817',
          red:    '#f05252',
          purple: '#a78bfa',
          cyan:   '#22d3ee',
        },
        border: '#1e2a3a',
        muted:  '#6b7a99',
      },
      fontFamily: {
        mono: ['JetBrains Mono', 'Fira Code', 'monospace'],
      },
      backgroundImage: {
        'gradient-brand':  'linear-gradient(135deg, #4d9eff 0%, #a78bfa 100%)',
        'gradient-subtle': 'linear-gradient(135deg, #0f1520 0%, #161e2e 100%)',
        'gradient-card':   'linear-gradient(145deg, #161e2e 0%, #0f1520 100%)',
      },
      keyframes: {
        'fade-in': {
          '0%':   { opacity: '0', transform: 'translateY(6px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        'slide-up': {
          '0%':   { opacity: '0', transform: 'translateY(16px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        pulse3: {
          '0%, 80%, 100%': { opacity: '0.2', transform: 'scale(0.8)' },
          '40%':           { opacity: '1',   transform: 'scale(1)' },
        },
        shimmer: {
          '0%':   { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
      },
      animation: {
        'fade-in':  'fade-in 0.25s ease-out',
        'slide-up': 'slide-up 0.3s ease-out',
        pulse3:     'pulse3 1.4s ease-in-out infinite',
        shimmer:    'shimmer 2s linear infinite',
      },
    },
  },
  plugins: [],
}
