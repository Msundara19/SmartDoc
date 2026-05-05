import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { listDocuments, deleteDocument } from '../api/documents'
import StatusBadge from '../components/StatusBadge'
import Spinner from '../components/Spinner'
import type { Document } from '../types'

const TYPE_META: Record<string, { label: string; color: string; bg: string }> = {
  research_paper: { label: 'Research Paper', color: 'text-accent-blue',   bg: 'bg-accent-blue/10 border-accent-blue/20' },
  legal:          { label: 'Legal',          color: 'text-accent-yellow', bg: 'bg-accent-yellow/10 border-accent-yellow/20' },
  technical:      { label: 'Technical',      color: 'text-accent-green',  bg: 'bg-accent-green/10 border-accent-green/20' },
  general:        { label: 'General',        color: 'text-accent-purple', bg: 'bg-accent-purple/10 border-accent-purple/20' },
}

function fmt(bytes: number | null) {
  if (!bytes) return '—'
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function relativeTime(iso: string) {
  const diff = (Date.now() - new Date(iso).getTime()) / 1000
  if (diff < 60)    return 'just now'
  if (diff < 3600)  return `${Math.floor(diff / 60)}m ago`
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`
  return new Date(iso).toLocaleDateString()
}

function DocCard({ doc, onDelete, deleting, onQuery }: {
  doc: Document
  onDelete: () => void
  deleting: boolean
  onQuery: () => void
}) {
  const type = TYPE_META[doc.docType ?? '']

  return (
    <div className="card p-4 flex flex-col gap-3 hover:bg-surface-2/50 transition-colors group animate-fade-in">
      {/* Header */}
      <div className="flex items-start gap-3">
        <div className="w-9 h-9 rounded-lg bg-accent-red/10 border border-accent-red/20 flex items-center justify-center shrink-0">
          <svg className="w-4 h-4 text-accent-red" fill="currentColor" viewBox="0 0 24 24">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
            <path d="M14 2v6h6" fill="none" stroke="rgba(255,255,255,0.3)" strokeWidth="1.5" />
          </svg>
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-100 truncate leading-tight">{doc.filename}</p>
          <p className="text-[11px] text-muted mt-0.5">{relativeTime(doc.uploadTime)}</p>
        </div>
        <StatusBadge status={doc.status} />
      </div>

      {/* Meta */}
      <div className="flex items-center gap-2 flex-wrap">
        {type && (
          <span className={`text-[10px] px-2 py-0.5 rounded-full border font-medium ${type.color} ${type.bg}`}>
            {type.label}
          </span>
        )}
        {doc.pageCount && (
          <span className="tag">{doc.pageCount} pages</span>
        )}
        <span className="tag">{fmt(doc.fileSizeBytes)}</span>
      </div>

      {doc.status === 'failed' && doc.errorMessage && (
        <p className="text-[11px] text-accent-red bg-red-900/10 rounded px-2 py-1 truncate">
          {doc.errorMessage}
        </p>
      )}

      {/* Actions */}
      <div className="flex items-center gap-2 pt-1 border-t border-border">
        {doc.status === 'ready' && (
          <button
            className="flex-1 py-1.5 text-xs font-medium rounded-lg border border-accent-blue/40 text-accent-blue hover:bg-accent-blue hover:text-surface-0 transition-colors"
            onClick={onQuery}
          >
            Query →
          </button>
        )}
        <button
          className="btn-danger text-xs ml-auto"
          onClick={onDelete}
          disabled={deleting}
        >
          {deleting ? <Spinner size={12} /> : (
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
            </svg>
          )}
        </button>
      </div>
    </div>
  )
}

export default function LibraryPage() {
  const navigate = useNavigate()
  const [docs, setDocs] = useState<Document[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    setError('')
    try { setDocs(await listDocuments()) }
    catch (err) { setError(err instanceof Error ? err.message : 'Failed to load.') }
    finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  async function handleDelete(id: string) {
    if (!confirm('Delete this document and all its indexed chunks?')) return
    setDeletingId(id)
    try {
      await deleteDocument(id)
      setDocs(prev => prev.filter(d => d.documentId !== id))
    } catch (err) { alert(err instanceof Error ? err.message : 'Delete failed.') }
    finally { setDeletingId(null) }
  }

  const ready  = docs.filter(d => d.status === 'ready').length
  const failed = docs.filter(d => d.status === 'failed').length

  return (
    <div className="p-8 space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-gray-100">Document Library</h1>
          <div className="flex items-center gap-3 mt-1.5">
            {loading ? (
              <span className="text-sm text-muted">Loading…</span>
            ) : (
              <>
                <span className="text-sm text-muted">{docs.length} document{docs.length !== 1 ? 's' : ''}</span>
                {ready > 0   && <span className="tag text-accent-green border-accent-green/30">{ready} ready</span>}
                {failed > 0  && <span className="tag text-accent-red border-accent-red/30">{failed} failed</span>}
              </>
            )}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button className="btn-ghost text-sm" onClick={() => navigate('/')}>
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} d="M12 4v16m8-8H4" />
            </svg>
            Upload
          </button>
          <button className="btn-ghost text-sm" onClick={load} disabled={loading}>
            {loading ? <Spinner size={14} /> : (
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
            )}
          </button>
        </div>
      </div>

      {/* Loading */}
      {loading && (
        <div className="flex items-center justify-center py-20 gap-3 text-muted">
          <Spinner size={18} /><span>Loading documents…</span>
        </div>
      )}

      {/* Error */}
      {!loading && error && (
        <div className="card p-5 border-accent-red/30 bg-red-900/10 flex gap-3 items-start">
          <svg className="w-5 h-5 text-accent-red shrink-0" fill="currentColor" viewBox="0 0 20 20">
            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" />
          </svg>
          <div>
            <p className="text-accent-red text-sm">{error}</p>
            <button className="btn-ghost text-xs mt-2" onClick={load}>Retry</button>
          </div>
        </div>
      )}

      {/* Empty */}
      {!loading && !error && docs.length === 0 && (
        <div className="flex flex-col items-center justify-center py-24 gap-5 text-center">
          <div className="w-16 h-16 rounded-2xl bg-surface-2 border border-border flex items-center justify-center">
            <svg className="w-7 h-7 text-surface-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414A1 1 0 0119 9.414V19a2 2 0 01-2 2z" />
            </svg>
          </div>
          <div>
            <p className="text-gray-200 font-medium">No documents indexed yet</p>
            <p className="text-muted text-sm mt-1">Upload a PDF to build your knowledge base.</p>
          </div>
          <button className="btn-primary" onClick={() => navigate('/')}>Upload first document</button>
        </div>
      )}

      {/* Grid */}
      {!loading && !error && docs.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-3">
          {docs.map(doc => (
            <DocCard
              key={doc.documentId}
              doc={doc}
              onDelete={() => handleDelete(doc.documentId)}
              deleting={deletingId === doc.documentId}
              onQuery={() => navigate(`/chat/${doc.documentId}`)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
