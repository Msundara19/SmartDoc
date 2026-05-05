import clsx from 'clsx'

interface Props {
  label: 'High' | 'Medium' | 'Low' | 'Insufficient'
  score: number
}

const config = {
  High: { color: 'text-accent-green border-accent-green bg-green-900/20', dot: 'bg-accent-green' },
  Medium: { color: 'text-accent-yellow border-accent-yellow bg-yellow-900/20', dot: 'bg-accent-yellow' },
  Low: { color: 'text-orange-400 border-orange-400 bg-orange-900/20', dot: 'bg-orange-400' },
  Insufficient: { color: 'text-accent-red border-accent-red bg-red-900/20', dot: 'bg-accent-red' },
}

export default function ConfidenceBadge({ label, score }: Props) {
  const { color, dot } = config[label]
  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full border text-xs font-medium',
        color,
      )}
    >
      <span className={clsx('w-1.5 h-1.5 rounded-full', dot)} />
      {label} · {(score * 100).toFixed(0)}%
    </span>
  )
}
