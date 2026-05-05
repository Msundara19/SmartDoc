import clsx from 'clsx'

interface Props {
  status: 'pending' | 'processing' | 'ready' | 'failed'
}

const cfg = {
  pending:    { label: 'Pending',    cls: 'text-muted border-muted/40 bg-surface-3' },
  processing: { label: 'Processing', cls: 'text-accent-blue border-accent-blue/40 bg-accent-blue/10 animate-pulse' },
  ready:      { label: 'Ready',      cls: 'text-accent-green border-accent-green/40 bg-accent-green/10' },
  failed:     { label: 'Failed',     cls: 'text-accent-red border-accent-red/40 bg-accent-red/10' },
}

export default function StatusBadge({ status }: Props) {
  const { label, cls } = cfg[status]
  return (
    <span className={clsx('px-2 py-0.5 rounded-full border text-[11px] font-medium', cls)}>
      {label}
    </span>
  )
}
