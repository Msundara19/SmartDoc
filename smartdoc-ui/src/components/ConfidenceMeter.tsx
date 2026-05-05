import clsx from 'clsx'

interface Props {
  label: 'High' | 'Medium' | 'Low' | 'Insufficient'
  score: number
  showBar?: boolean
}

const cfg = {
  High:         { color: 'text-accent-green',  bar: 'bg-accent-green',  bg: 'bg-green-900/20',  border: 'border-accent-green/30'  },
  Medium:       { color: 'text-accent-blue',   bar: 'bg-accent-blue',   bg: 'bg-blue-900/20',   border: 'border-accent-blue/30'   },
  Low:          { color: 'text-accent-yellow', bar: 'bg-accent-yellow', bg: 'bg-yellow-900/20', border: 'border-accent-yellow/30' },
  Insufficient: { color: 'text-accent-red',    bar: 'bg-accent-red',    bg: 'bg-red-900/20',    border: 'border-accent-red/30'    },
}

export default function ConfidenceMeter({ label, score, showBar = false }: Props) {
  const { color, bar, bg, border } = cfg[label]
  const pct = Math.round(score * 100)

  if (showBar) {
    return (
      <div className={clsx('rounded-lg px-3 py-2 border', bg, border)}>
        <div className="flex items-center justify-between mb-1.5">
          <span className={clsx('text-xs font-semibold', color)}>{label}</span>
          <span className={clsx('text-xs font-mono', color)}>{pct}%</span>
        </div>
        <div className="h-1 rounded-full bg-surface-3">
          <div
            className={clsx('h-1 rounded-full transition-all duration-700', bar)}
            style={{ width: `${pct}%` }}
          />
        </div>
      </div>
    )
  }

  return (
    <span className={clsx('inline-flex items-center gap-1.5 text-xs font-medium', color)}>
      <span className={clsx('w-1.5 h-1.5 rounded-full', bar)} />
      {label} · {pct}%
    </span>
  )
}
