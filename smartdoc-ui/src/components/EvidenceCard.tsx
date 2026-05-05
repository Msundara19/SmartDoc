import clsx from 'clsx'
import type { EvidenceItem } from '../types'

interface Props {
  item: EvidenceItem
  rank: number
}

function scoreColor(score: number) {
  if (score >= 0.68) return 'border-l-accent-green'
  if (score >= 0.60) return 'border-l-accent-blue'
  if (score >= 0.52) return 'border-l-accent-yellow'
  return 'border-l-muted'
}

export default function EvidenceCard({ item, rank }: Props) {
  return (
    <div className={clsx('bg-surface-2 border border-border border-l-2 rounded-lg p-3 space-y-2 animate-fade-in', scoreColor(item.similarityScore))}>
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-[10px] font-mono text-muted bg-surface-3 px-1.5 py-0.5 rounded">
            #{rank}
          </span>
          {item.section && (
            <span className="text-[10px] px-1.5 py-0.5 rounded bg-accent-purple/10 border border-accent-purple/20 text-accent-purple">
              {item.section}
            </span>
          )}
          {item.page && (
            <span className="text-[10px] text-muted">pg. {item.page}</span>
          )}
        </div>
        <div className="flex items-center gap-2 text-[10px] font-mono text-muted">
          <span title="Cosine similarity">{(item.similarityScore * 100).toFixed(1)}% vec</span>
          {item.bm25Score > 0 && (
            <span title="BM25 keyword score" className="text-accent-blue">
              {item.bm25Score.toFixed(4)} bm25
            </span>
          )}
        </div>
      </div>
      <p className="text-xs text-gray-400 leading-relaxed line-clamp-4 font-mono">
        {item.chunkText}
      </p>
    </div>
  )
}
