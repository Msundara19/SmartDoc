export default function Spinner({ size = 16 }: { size?: number }) {
  return (
    <svg
      className="animate-spin text-accent-blue shrink-0"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2.5}
    >
      <circle cx="12" cy="12" r="10" strokeOpacity={0.15} />
      <path d="M12 2a10 10 0 0 1 10 10" strokeLinecap="round" />
    </svg>
  )
}

export function TypingDots() {
  return (
    <div className="dot-flashing flex items-center gap-1">
      <span /><span /><span />
    </div>
  )
}
