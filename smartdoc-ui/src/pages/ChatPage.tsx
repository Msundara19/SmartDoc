import { useState, useEffect, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { getDocumentStatus, queryDocument } from '../api/documents'
import ConfidenceMeter from '../components/ConfidenceMeter'
import EvidenceCard from '../components/EvidenceCard'
import Spinner, { TypingDots } from '../components/Spinner'
import type { Document, QueryResponse } from '../types'

interface QA { question: string; response: QueryResponse; ts: Date }

const TYPE_LABEL: Record<string, string> = {
  research_paper: 'Research Paper',
  legal: 'Legal',
  technical: 'Technical',
  general: 'General',
}

const SUGGESTED = [
  'What are the main topics covered?',
  'What methodology is used?',
  'What are the key findings?',
  'What is the conclusion?',
]

function AiAvatar() {
  return (
    <div className="w-7 h-7 rounded-lg bg-gradient-brand flex items-center justify-center shrink-0 mt-0.5">
      <svg className="w-3.5 h-3.5 text-white" fill="currentColor" viewBox="0 0 24 24">
        <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414A1 1 0 0119 9.414V19a2 2 0 01-2 2z" />
      </svg>
    </div>
  )
}

export default function ChatPage() {
  const { documentId } = useParams<{ documentId: string }>()
  const navigate = useNavigate()

  const [doc, setDoc] = useState<Document | null>(null)
  const [docError, setDocError] = useState('')
  const [question, setQuestion] = useState('')
  const [history, setHistory] = useState<QA[]>([])
  const [querying, setQuerying] = useState(false)
  const [queryError, setQueryError] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!documentId) return
    getDocumentStatus(documentId)
      .then(setDoc)
      .catch(err => setDocError(err instanceof Error ? err.message : 'Failed to load document.'))
  }, [documentId])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [history, querying])

  async function submit(q: string) {
    if (!q.trim() || !documentId || querying) return
    setQuestion('')
    setQuerying(true)
    setQueryError('')
    try {
      const response = await queryDocument(documentId, q.trim())
      setHistory(prev => [...prev, { question: q.trim(), response, ts: new Date() }])
    } catch (err) {
      setQueryError(err instanceof Error ? err.message : 'Query failed.')
    } finally {
      setQuerying(false)
      setTimeout(() => inputRef.current?.focus(), 50)
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    submit(question)
  }

  if (docError) {
    return (
      <div className="p-8">
        <div className="card p-6 border-accent-red/30 bg-red-900/10 space-y-3 max-w-lg">
          <p className="text-accent-red">{docError}</p>
          <button className="btn-ghost text-sm" onClick={() => navigate('/library')}>← Back to library</button>
        </div>
      </div>
    )
  }

  if (!doc) {
    return (
      <div className="flex items-center gap-3 text-muted p-8">
        <Spinner size={18} /> Loading document…
      </div>
    )
  }

  return (
    <div className="flex flex-col h-screen">
      {/* Top bar */}
      <div className="flex items-center gap-3 px-5 py-3 border-b border-border bg-surface-1 shrink-0">
        <button className="btn-ghost text-xs gap-1.5" onClick={() => navigate('/library')}>
          <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
          </svg>
          Library
        </button>

        <div className="h-4 w-px bg-border" />

        <div className="w-6 h-6 rounded bg-accent-red/10 border border-accent-red/20 flex items-center justify-center shrink-0">
          <svg className="w-3 h-3 text-accent-red" fill="currentColor" viewBox="0 0 24 24">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
          </svg>
        </div>

        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-100 truncate leading-none">{doc.filename}</p>
          <p className="text-[11px] text-muted mt-0.5">
            {doc.pageCount} pages · {TYPE_LABEL[doc.docType ?? ''] ?? 'Document'}
          </p>
        </div>

        <button
          className="btn-ghost text-xs gap-1.5 border border-accent-purple/30 text-accent-purple
            hover:bg-accent-purple/10 hover:text-accent-purple shrink-0"
          onClick={() => navigate(`/chat/${documentId}/flashcards`)}
          title="Generate flashcards for key topics"
        >
          <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
              d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
          </svg>
          Flashcards
        </button>

        <div className="flex items-center gap-1.5 text-[11px] text-accent-green border border-accent-green/30 bg-accent-green/10 px-2 py-0.5 rounded-full">
          <span className="w-1.5 h-1.5 rounded-full bg-accent-green" />
          Ready
        </div>
      </div>

      {/* Chat area */}
      <div className="flex-1 overflow-y-auto px-5 py-4 space-y-5">
        {history.length === 0 && !querying && (
          <div className="flex flex-col items-center justify-center h-full gap-6 text-center py-12 animate-fade-in">
            <div className="w-14 h-14 rounded-2xl bg-gradient-brand flex items-center justify-center shadow-lg">
              <svg className="w-6 h-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8}
                  d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
              </svg>
            </div>
            <div>
              <p className="text-gray-100 font-semibold text-lg">Ask anything about this document</p>
              <p className="text-muted text-sm mt-1.5">
                Every answer includes a confidence score and source evidence.
              </p>
            </div>
            <div className="flex flex-wrap gap-2 justify-center max-w-lg">
              {SUGGESTED.map(s => (
                <button
                  key={s}
                  onClick={() => submit(s)}
                  className="text-xs text-muted border border-border rounded-lg px-3 py-1.5 hover:text-gray-100 hover:border-surface-4 hover:bg-surface-2 transition-colors"
                >
                  {s}
                </button>
              ))}
            </div>
          </div>
        )}

        {history.map((qa, i) => (
          <div key={i} className="space-y-2 animate-slide-up">
            {/* User bubble */}
            <div className="flex justify-end">
              <div className="max-w-xl bg-surface-3 border border-border rounded-2xl rounded-tr-sm px-4 py-2.5 text-sm text-gray-100">
                {qa.question}
              </div>
            </div>

            {/* AI response */}
            <div className="flex gap-3 max-w-3xl">
              <AiAvatar />
              <div className="flex-1 space-y-3">
                {/* Rejection banner */}
                {qa.response.rejectionReason && (
                  <div className="rounded-xl bg-red-900/10 border border-accent-red/20 px-4 py-3 flex gap-3">
                    <svg className="w-4 h-4 text-accent-red shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" />
                    </svg>
                    <p className="text-sm text-gray-300 leading-relaxed">{qa.response.rejectionReason}</p>
                  </div>
                )}

                {/* Answer text */}
                {qa.response.answer && (
                  <div className="rounded-xl border border-surface-4 bg-surface-3 px-5 py-4 text-sm text-gray-100 leading-7 whitespace-pre-wrap">
                    {qa.response.answer}
                  </div>
                )}

                {/* Confidence + meta row */}
                <div className="space-y-2">
                  <ConfidenceMeter
                    label={qa.response.confidenceLabel}
                    score={qa.response.confidence}
                    showBar
                  />
                  <div className="flex items-center gap-3 text-[11px] text-muted px-1">
                    <span>{qa.response.retrievalCount} chunks searched</span>
                    <span>·</span>
                    <span>{qa.ts.toLocaleTimeString()}</span>
                  </div>
                </div>

                {/* Evidence accordion */}
                {qa.response.evidence.length > 0 && (
                  <details className="group">
                    <summary className="flex items-center gap-2 text-xs text-muted cursor-pointer select-none
                      hover:text-gray-300 transition-colors list-none">
                      <svg className="w-3 h-3 transition-transform group-open:rotate-90" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                      </svg>
                      {qa.response.evidence.length} source chunk{qa.response.evidence.length !== 1 ? 's' : ''}
                    </summary>
                    <div className="mt-2 space-y-2">
                      {qa.response.evidence.map((item, j) => (
                        <EvidenceCard key={j} item={item} rank={j + 1} />
                      ))}
                    </div>
                  </details>
                )}
              </div>
            </div>
          </div>
        ))}

        {/* Typing indicator */}
        {querying && (
          <div className="flex gap-3 animate-fade-in">
            <AiAvatar />
            <div className="card px-4 py-3 flex items-center gap-2 text-muted text-sm">
              <TypingDots />
              <span>Thinking…</span>
            </div>
          </div>
        )}

        {queryError && (
          <div className="card px-4 py-3 border-accent-red/30 text-accent-red text-sm animate-fade-in">
            {queryError}
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      {/* Input bar */}
      <div className="shrink-0 px-5 py-4 border-t border-border bg-surface-1">
        <form onSubmit={handleSubmit} className="flex gap-2 max-w-3xl mx-auto">
          <input
            ref={inputRef}
            className="input flex-1"
            placeholder="Ask a question about this document…"
            value={question}
            onChange={e => setQuestion(e.target.value)}
            disabled={querying}
            autoFocus
          />
          <button
            type="submit"
            className="btn-primary px-5 shrink-0"
            disabled={!question.trim() || querying}
          >
            {querying ? <Spinner size={16} /> : (
              <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24">
                <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z" />
              </svg>
            )}
          </button>
        </form>
      </div>
    </div>
  )
}
