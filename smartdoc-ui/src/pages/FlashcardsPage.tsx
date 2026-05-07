import { useState, useEffect, useCallback, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { getDocumentStatus, getFlashcards } from '../api/documents'
import Spinner from '../components/Spinner'
import type { Document, Flashcard } from '../types'

export default function FlashcardsPage() {
  const { documentId } = useParams<{ documentId: string }>()
  const navigate = useNavigate()

  const [doc, setDoc] = useState<Document | null>(null)
  const [cards, setCards] = useState<Flashcard[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [current, setCurrent] = useState(0)
  const [flipped, setFlipped] = useState(false)

  useEffect(() => {
    if (!documentId) return
    Promise.all([getDocumentStatus(documentId), getFlashcards(documentId)])
      .then(([docData, flashData]) => {
        setDoc(docData)
        setCards(flashData.cards)
      })
      .catch(err => setError(err instanceof Error ? err.message : 'Failed to generate flashcards.'))
      .finally(() => setLoading(false))
  }, [documentId])

  const touchStartX = useRef(0)

  const goTo = useCallback((index: number) => {
    setCurrent(index)
    setFlipped(false)
  }, [])

  // Keyboard navigation
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'ArrowLeft')  setCurrent(c => { const n = Math.max(0, c - 1); if (n !== c) setFlipped(false); return n })
      else if (e.key === 'ArrowRight') setCurrent(c => { const n = Math.min(cards.length - 1, c + 1); if (n !== c) setFlipped(false); return n })
      else if (e.key === ' ') { e.preventDefault(); setFlipped(f => !f) }
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [cards.length])

  if (loading) {
    return (
      <div className="flex flex-col h-screen">
        <div className="flex-1 flex flex-col items-center justify-center gap-4">
          <Spinner size={32} />
          <p className="text-sm text-gray-100">Generating flashcards…</p>
          <p className="text-xs text-muted">Analysing document content — this takes a few seconds</p>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="card p-6 border-accent-red/30 bg-red-900/10 space-y-3 max-w-lg">
          <p className="text-accent-red text-sm">{error}</p>
          <button className="btn-ghost text-sm" onClick={() => navigate(`/chat/${documentId}`)}>
            ← Back to chat
          </button>
        </div>
      </div>
    )
  }

  if (cards.length === 0) {
    return (
      <div className="p-8">
        <div className="card p-6 space-y-3 max-w-lg">
          <p className="text-muted text-sm">No flashcards could be generated for this document.</p>
          <button className="btn-ghost text-sm" onClick={() => navigate(`/chat/${documentId}`)}>
            ← Back to chat
          </button>
        </div>
      </div>
    )
  }

  const card = cards[current]

  return (
    <div className="flex flex-col h-screen">
      {/* Top bar */}
      <div className="flex items-center gap-3 px-5 py-3 border-b border-border bg-surface-1 shrink-0">
        <button className="btn-ghost text-xs gap-1.5" onClick={() => navigate(`/chat/${documentId}`)}>
          <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
          </svg>
          Chat
        </button>

        <div className="h-4 w-px bg-border" />

        <div className="w-6 h-6 rounded bg-accent-purple/10 border border-accent-purple/20 flex items-center justify-center shrink-0">
          <svg className="w-3 h-3 text-accent-purple" fill="currentColor" viewBox="0 0 24 24">
            <path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
          </svg>
        </div>

        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-100 truncate leading-none">{doc?.filename}</p>
          <p className="text-[11px] text-muted mt-0.5">Flashcards</p>
        </div>

        <div className="flex items-center gap-1.5 text-[11px] text-accent-purple border border-accent-purple/30 bg-accent-purple/10 px-2 py-0.5 rounded-full shrink-0">
          {cards.length} cards
        </div>
      </div>

      {/* Main */}
      <div className="flex-1 flex flex-col items-center justify-center px-6 py-8 gap-7 overflow-auto">

        {/* Progress bar */}
        <div className="w-full max-w-2xl space-y-1.5">
          <p className="text-xs text-muted text-center">
            Card {current + 1} of {cards.length}
          </p>
          <div className="flex gap-1">
            {cards.map((_, i) => (
              <button
                key={i}
                onClick={() => goTo(i)}
                className={`h-0.5 flex-1 rounded-full transition-colors ${
                  i <= current ? 'bg-accent-purple' : 'bg-surface-4'
                }`}
              />
            ))}
          </div>
        </div>

        {/* Flip card */}
        <div
          className="[perspective:1200px] w-full max-w-2xl cursor-pointer select-none"
          onClick={() => setFlipped(f => !f)}
          onTouchStart={e => { touchStartX.current = e.touches[0].clientX }}
          onTouchEnd={e => {
            const dx = e.changedTouches[0].clientX - touchStartX.current
            if (Math.abs(dx) < 10) return
            if (dx > 50 && current > 0) goTo(current - 1)
            else if (dx < -50 && current < cards.length - 1) goTo(current + 1)
          }}
        >
          <div
            className={`relative h-64 [transform-style:preserve-3d] transition-transform duration-500 ${
              flipped ? '[transform:rotateY(180deg)]' : ''
            }`}
          >
            {/* Front face */}
            <div className="absolute inset-0 [backface-visibility:hidden] card p-8 flex flex-col items-center justify-center gap-5">
              <div className="w-9 h-9 rounded-xl bg-accent-purple/10 border border-accent-purple/20 flex items-center justify-center shrink-0">
                <svg className="w-4 h-4 text-accent-purple" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <p className="text-lg font-medium text-center text-gray-100 leading-relaxed max-w-md">
                {card.front}
              </p>
              <p className="text-xs text-muted/50">click to reveal answer</p>
            </div>

            {/* Back face */}
            <div className="absolute inset-0 [backface-visibility:hidden] [transform:rotateY(180deg)] card p-8 flex flex-col items-center justify-center gap-4">
              <div className="w-9 h-9 rounded-xl bg-accent-green/10 border border-accent-green/20 flex items-center justify-center shrink-0">
                <svg className="w-4 h-4 text-accent-green" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <p className="text-sm text-center text-gray-100 leading-relaxed max-w-md">
                {card.back}
              </p>
              {(card.page || card.section) && (
                <div className="flex items-center gap-2 flex-wrap justify-center">
                  {card.page   && <span className="tag">Page {card.page}</span>}
                  {card.section && <span className="tag">{card.section}</span>}
                </div>
              )}
              <p className="text-xs text-muted/50">click to see question</p>
            </div>
          </div>
        </div>

        {/* Navigation */}
        <div className="flex items-center gap-6">
          <button
            onClick={() => goTo(current - 1)}
            disabled={current === 0}
            className="btn-ghost disabled:opacity-30 disabled:cursor-not-allowed"
          >
            ← Prev
          </button>

          <div className="flex gap-1.5 flex-wrap justify-center max-w-xs">
            {cards.map((_, i) => (
              <button
                key={i}
                onClick={() => goTo(i)}
                className={`w-2 h-2 rounded-full transition-all ${
                  i === current
                    ? 'bg-accent-purple scale-125'
                    : 'bg-surface-4 hover:bg-muted'
                }`}
              />
            ))}
          </div>

          <button
            onClick={() => goTo(current + 1)}
            disabled={current === cards.length - 1}
            className="btn-ghost disabled:opacity-30 disabled:cursor-not-allowed"
          >
            Next →
          </button>
        </div>

        <p className="text-xs text-muted/40 hidden sm:block">← → to navigate · Space to flip</p>
        <p className="text-xs text-muted/40 sm:hidden">swipe to navigate · tap to flip</p>
      </div>
    </div>
  )
}
