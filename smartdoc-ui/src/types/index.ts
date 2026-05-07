export interface Document {
  documentId: string
  filename: string
  status: 'pending' | 'processing' | 'ready' | 'failed'
  docType: string | null
  pageCount: number | null
  uploadTime: string
  fileSizeBytes: number | null
  errorMessage?: string | null
}

export interface EvidenceItem {
  chunkText: string
  page: number | null
  section: string | null
  similarityScore: number
  bm25Score: number
  hybridScore: number
}

export interface QueryResponse {
  answer: string
  confidence: number
  confidenceLabel: 'High' | 'Medium' | 'Low' | 'Insufficient'
  evidence: EvidenceItem[]
  retrievalCount: number
  rejectionReason: string | null
}

export interface ConversationMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface Flashcard {
  front: string
  back: string
  page: number | null
  section: string | null
}

export interface FlashcardsResponse {
  cards: Flashcard[]
  chunksUsed: number
}
