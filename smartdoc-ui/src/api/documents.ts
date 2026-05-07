import type { Document, QueryResponse, FlashcardsResponse, ConversationMessage } from '../types'

const API_ROOT = import.meta.env.VITE_API_BASE_URL ?? ''
const BASE = `${API_ROOT}/api/documents`

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init)
  if (!res.ok) {
    let message = `Request failed: ${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      message = body?.detail ?? body?.error ?? body?.title ?? message
    } catch {
      // ignore parse error
    }
    throw new Error(message)
  }
  if (res.status === 204) return undefined as unknown as T
  return res.json() as Promise<T>
}

export async function uploadDocument(file: File): Promise<{ documentId: string; status: string }> {
  const form = new FormData()
  form.append('file', file)
  return request(`${BASE}/upload`, { method: 'POST', body: form })
}

export async function getDocumentStatus(id: string): Promise<Document> {
  return request(`${BASE}/${id}/status`)
}

export async function listDocuments(): Promise<Document[]> {
  return request(BASE)
}

export async function queryDocument(
  id: string,
  question: string,
  history: ConversationMessage[] = []
): Promise<QueryResponse> {
  return request(`${BASE}/${id}/query`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, history }),
  })
}

export async function deleteDocument(id: string): Promise<void> {
  return request(`${BASE}/${id}`, { method: 'DELETE' })
}

export async function getFlashcards(id: string): Promise<FlashcardsResponse> {
  return request(`${BASE}/${id}/flashcards`)
}
