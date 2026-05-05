import { useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useDropzone } from 'react-dropzone'
import { uploadDocument, getDocumentStatus } from '../api/documents'
import { usePolling } from '../hooks/usePolling'
import StatusBadge from '../components/StatusBadge'
import Spinner from '../components/Spinner'
import type { Document } from '../types'

type UploadState = 'idle' | 'uploading' | 'polling' | 'done' | 'error'

const DOC_TYPE_META: Record<string, { label: string; color: string; desc: string }> = {
  research_paper: { label: 'Research Paper',   color: 'text-accent-blue',   desc: 'Chunked by section headings' },
  legal:          { label: 'Legal / Contract', color: 'text-accent-yellow', desc: 'Chunked by article & clause boundaries' },
  technical:      { label: 'Technical',        color: 'text-accent-green',  desc: 'Chunked by numbered section headings' },
  general:        { label: 'General Document', color: 'text-accent-purple', desc: 'Chunked by paragraph boundaries' },
}

const STEPS = [
  { key: 'uploading',   label: 'Uploading file',                        icon: '↑' },
  { key: 'processing',  label: 'Structure-aware chunking + embedding',   icon: '⚙' },
  { key: 'ready',       label: 'Indexed and ready',                      icon: '✓' },
] as const

export default function UploadPage() {
  const navigate = useNavigate()
  const [state, setState] = useState<UploadState>('idle')
  const [errorMsg, setErrorMsg] = useState('')
  const [doc, setDoc] = useState<Document | null>(null)

  const onDrop = useCallback(async (accepted: File[]) => {
    const file = accepted[0]
    if (!file) return
    setState('uploading')
    setErrorMsg('')
    try {
      const { documentId } = await uploadDocument(file)
      const initial = await getDocumentStatus(documentId)
      setDoc(initial)
      setState('polling')
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : 'Upload failed.')
      setState('error')
    }
  }, [])

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: { 'application/pdf': ['.pdf'] },
    multiple: false,
    disabled: state === 'uploading' || state === 'polling',
  })

  usePolling(async () => {
    if (!doc) return true
    try {
      const updated = await getDocumentStatus(doc.documentId)
      setDoc(updated)
      if (updated.status === 'ready' || updated.status === 'failed') {
        setState('done')
        return true
      }
      return false
    } catch { return false }
  }, 2000, state === 'polling')

  const typeInfo = DOC_TYPE_META[doc?.docType ?? '']

  return (
    <div className="flex h-full min-h-screen">
      {/* Left panel — hero */}
      <div className="hidden lg:flex w-80 shrink-0 flex-col justify-between p-8 border-r border-border bg-surface-1">
        <div className="space-y-8">
          <div>
            <h2 className="text-[11px] font-semibold text-muted uppercase tracking-widest mb-6">How it works</h2>
            {[
              { step: '01', title: 'Upload PDF', desc: 'Any research paper, legal doc, or general document up to 50 MB.' },
              { step: '02', title: 'Structure detected', desc: 'Headings, clauses, and paragraphs are parsed — not fixed windows.' },
              { step: '03', title: 'Embeddings stored', desc: 'Each chunk is embedded via nomic-embed-text and stored in pgvector.' },
              { step: '04', title: 'Ask questions', desc: 'Confidence-gated retrieval — no hallucinations on out-of-scope queries.' },
            ].map(({ step, title, desc }) => (
              <div key={step} className="flex gap-4 mb-6">
                <span className="text-gradient font-bold text-base w-7 shrink-0 mt-0.5">{step}</span>
                <div>
                  <p className="text-gray-100 text-sm font-semibold">{title}</p>
                  <p className="text-muted text-xs mt-1 leading-relaxed">{desc}</p>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="rounded-xl border border-border bg-surface-2 p-4 text-xs text-muted space-y-2">
          <p className="text-gray-300 font-semibold text-sm mb-3">Tech Stack</p>
          {[
            { dot: 'bg-accent-blue',   label: 'ASP.NET Core 8 · pgvector' },
            { dot: 'bg-accent-purple', label: 'nomic-embed-text (768d)' },
            { dot: 'bg-accent-green',  label: 'Groq · llama-3.3-70b' },
          ].map(({ dot, label }) => (
            <p key={label} className="flex items-center gap-2">
              <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${dot}`} />
              {label}
            </p>
          ))}
        </div>
      </div>

      {/* Right panel — upload */}
      <div className="flex-1 flex flex-col p-8 gap-6 max-w-2xl">
        <div>
          <h1 className="text-2xl font-semibold text-gray-100">Upload Document</h1>
          <p className="text-sm text-muted mt-1">Drag and drop a PDF to begin structure-aware ingestion.</p>
        </div>

        {/* Drop zone */}
        <div
          {...getRootProps()}
          className={`relative rounded-2xl border-2 border-dashed p-16 text-center cursor-pointer
            transition-all duration-200 group
            ${isDragActive
              ? 'border-accent-blue bg-accent-blue/8 glow-blue'
              : 'border-surface-4 bg-surface-1 hover:border-accent-blue/40 hover:bg-surface-2'
            }
            ${state === 'uploading' || state === 'polling' ? 'opacity-40 pointer-events-none' : ''}`}
        >
          <input {...getInputProps()} />

          {isDragActive && (
            <div className="absolute inset-0 rounded-2xl border-2 border-accent-blue animate-ping opacity-20 pointer-events-none" />
          )}

          <div className="flex flex-col items-center gap-5">
            <div className={`w-20 h-20 rounded-2xl flex items-center justify-center transition-all duration-200
              ${isDragActive
                ? 'bg-accent-blue/20 scale-110'
                : 'bg-surface-3 group-hover:bg-surface-4 group-hover:scale-105'}`}>
              <svg className={`w-9 h-9 transition-colors ${isDragActive ? 'text-accent-blue' : 'text-muted group-hover:text-gray-300'}`}
                fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                  d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414A1 1 0 0119 9.414V19a2 2 0 01-2 2z" />
              </svg>
            </div>
            {isDragActive ? (
              <p className="text-accent-blue font-semibold">Release to upload</p>
            ) : (
              <>
                <div>
                  <p className="text-gray-200 font-medium">Drop a PDF here or <span className="text-accent-blue">browse files</span></p>
                  <p className="text-muted text-sm mt-1">PDF only · max 50 MB</p>
                </div>
              </>
            )}
          </div>
        </div>

        {/* Error */}
        {state === 'error' && (
          <div className="card p-4 border-accent-red/30 bg-red-900/10 flex items-start gap-3 animate-fade-in">
            <svg className="w-4 h-4 text-accent-red shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" />
            </svg>
            <div className="flex-1">
              <p className="text-accent-red text-sm">{errorMsg}</p>
              <button className="btn-ghost text-xs mt-2 px-2" onClick={() => setState('idle')}>Try again</button>
            </div>
          </div>
        )}

        {/* Progress card */}
        {doc && state !== 'idle' && state !== 'error' && (
          <div className="card p-5 space-y-5 animate-slide-up">
            {/* File row */}
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-lg bg-accent-red/10 border border-accent-red/20 flex items-center justify-center shrink-0">
                <svg className="w-5 h-5 text-accent-red" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                  <path d="M14 2v6h6" fill="none" stroke="rgba(255,255,255,0.4)" strokeWidth="1.5" />
                </svg>
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-medium text-gray-100 truncate text-sm">{doc.filename}</p>
                <p className="text-xs text-muted">
                  {doc.fileSizeBytes ? `${(doc.fileSizeBytes / 1024).toFixed(0)} KB` : ''}
                </p>
              </div>
              <StatusBadge status={doc.status} />
            </div>

            {/* Steps */}
            <div className="relative space-y-3">
              {STEPS.map(({ key, label }) => {
                const isActive = doc.status === key || (key === 'uploading' && state === 'uploading')
                const isDone =
                  (key === 'uploading' && ['processing', 'ready'].includes(doc.status)) ||
                  (key === 'processing' && doc.status === 'ready') ||
                  (key === 'ready' && doc.status === 'ready')

                return (
                  <div key={key} className="flex items-center gap-3">
                    <div className={`w-6 h-6 rounded-full flex items-center justify-center text-xs shrink-0 border transition-colors
                      ${isDone ? 'bg-accent-green/20 border-accent-green/40 text-accent-green'
                        : isActive ? 'bg-accent-blue/20 border-accent-blue/40'
                        : 'bg-surface-3 border-border'}`}>
                      {isDone
                        ? <svg className="w-3 h-3" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" /></svg>
                        : isActive ? <Spinner size={12} />
                        : <span className="w-1.5 h-1.5 rounded-full bg-surface-4" />
                      }
                    </div>
                    <span className={`text-sm transition-colors
                      ${isDone ? 'text-accent-green' : isActive ? 'text-gray-100' : 'text-muted'}`}>
                      {label}
                    </span>
                  </div>
                )
              })}
            </div>

            {/* Success state */}
            {doc.status === 'ready' && (
              <div className="pt-4 border-t border-border space-y-4 animate-fade-in">
                {typeInfo && (
                  <div className="grid grid-cols-3 gap-3">
                    {[
                      { label: 'Type',     value: typeInfo.label, color: typeInfo.color },
                      { label: 'Pages',    value: String(doc.pageCount ?? '—'), color: 'text-gray-100' },
                      { label: 'Strategy', value: typeInfo.desc,  color: 'text-muted' },
                    ].map(({ label, value, color }) => (
                      <div key={label} className="bg-surface-2 rounded-lg p-3">
                        <p className="text-[10px] text-muted uppercase tracking-wider mb-1">{label}</p>
                        <p className={`text-xs font-medium ${color}`}>{value}</p>
                      </div>
                    ))}
                  </div>
                )}
                <button
                  className="btn-primary w-full py-2.5"
                  onClick={() => navigate(`/chat/${doc.documentId}`)}
                >
                  Start querying →
                </button>
              </div>
            )}

            {doc.status === 'failed' && (
              <div className="pt-3 border-t border-border text-sm text-accent-red animate-fade-in">
                Ingestion failed: {doc.errorMessage ?? 'Unknown error.'}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
