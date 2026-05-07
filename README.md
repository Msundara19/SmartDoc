# SmartDoc — Intelligent PDF Q&A

> A production-grade Retrieval-Augmented Generation (RAG) system that answers questions about PDF documents with **verifiable confidence scores** and **zero hallucination** on out-of-scope queries.

**Live demo:** [smart-doc-chi.vercel.app](https://smart-doc-chi.vercel.app)

---

## Why I Built This

Tools like ChatPDF, Adobe AI Assistant, and generic LLM wrappers let you "chat with your PDF" — but they all share the same fundamental problems:

| Problem | What current tools do | What SmartDoc does |
|---|---|---|
| **Blind chunking** | Split every document into fixed 512-token windows, breaking sentences and ideas mid-thought | Detects document type first, then splits on structural boundaries (headings, clauses, paragraphs) |
| **Hallucinated answers** | Call the LLM regardless of how relevant the retrieved content is | Gate the LLM behind a semantic similarity threshold — no call, no hallucination |
| **No transparency** | Return an answer with no signal of how confident or grounded it is | Every answer returns a numerical confidence score, label, and the exact source chunks used |
| **Keyword OR vector** | Search by either exact keywords or semantic similarity, never both | Fuse both signals via Reciprocal Rank Fusion — catches queries that need exact term matching AND semantic understanding |
| **One-size-fits-all** | Apply the same chunking to a legal contract and a research paper | Four document-type-specific chunking strategies, each tuned to that document's structure |

I built SmartDoc to demonstrate that the quality gap between a demo RAG app and a production-quality one comes down to these decisions — not the LLM.

---

## What I Built

A full-stack document intelligence platform:

- **Upload** any PDF (research papers, technical docs, legal contracts, general documents)
- **Ingestion pipeline** extracts text, detects document structure, chunks intelligently, embeds, and indexes into a vector + full-text search database
- **Q&A** with confidence scoring — the system tells you how reliable each answer is, shows you the source chunks it used, and refuses to answer questions the document can't support
- **Conversational memory** — multi-turn chat with 3-turn history, enabling follow-up questions like "elaborate on that" or "what page is that on?"
- **Document-specific query suggestions** — 6 questions generated from the document's content shown on load, eliminating cold-start friction
- **Flashcard generation** — automatically extracts key concepts from the document into study cards, cached after first generation
- **Semantic document summaries** — each document gets a 3–5 sentence prose summary generated at ingestion time and displayed on the library card, so you can assess relevance without opening the document
- **Library** to manage multiple documents

---

## The USPs

### USP 1 — Structure-Aware Chunking

Most RAG systems treat every document the same way. SmartDoc doesn't.

The pipeline first classifies the document into one of four types using heuristics on the extracted text, then applies a tailored chunking strategy:

| Document Type | Detection Signal | Chunking Strategy |
|---|---|---|
| **Technical** | ≥3 pages with numbered section headings (e.g. `3.2 Methods`) | Split on numbered heading boundaries |
| **Research Paper** | ≥3 standard headings (Abstract, Methods, Results, Conclusion) | Split on heading boundaries |
| **Legal / Contract** | Legal keyword density >0.5% or Article/Section/Clause regex | Split on Article, Section, Clause markers |
| **General** | None of the above | Paragraph boundaries, merge fragments <100 chars |

Additional pipeline hardening:
- **TOC filtering** — table of contents pages (>25% dot-leader characters) are discarded before chunking, preventing hundreds of near-identical junk fragments
- **Junk chunk filtering** — chunks under 40 characters or with >20% dot content are dropped
- **Synthetic overview chunk** — a summary listing all section names is prepended at ingestion time, so broad meta-questions ("What are the main topics?") retrieve a representative passage instead of a random interior fragment
- **Sub-splitting** — any structural chunk over 800 tokens is split with 50-token overlap, inheriting the parent's section name

**Result**: on a 24-page computer vision technical document, naive chunking produced 102 junk TOC fragments out of 129 total chunks. SmartDoc produced 22 clean, section-labeled chunks.

### USP 2 — Hybrid Retrieval + Explainable Confidence Scoring

**Retrieval** fuses two independent signals via Reciprocal Rank Fusion (k=60):

| Signal | Index | Strengths |
|---|---|---|
| Vector search | ivfflat cosine (pgvector) | Semantic similarity — finds conceptually related passages even with different wording |
| BM25 full-text | GIN on `tsvector` (PostgreSQL) | Exact term matching — catches acronyms, model names, and specific numbers |

Neither signal alone is sufficient. A query like "What is the mAP score?" needs BM25 to find "mAP" as an exact term, while "What were the performance improvements?" needs vector search to find semantically related content.

**Confidence gate** uses the raw cosine similarity of the top-ranked chunk to decide whether to call the LLM at all:

| Score | Label | Behaviour |
|---|---|---|
| < 0.30 | **Insufficient** | LLM never called — `rejectionReason` returned instead |
| 0.30 – 0.40 | **Low** | LLM called, disclaimer prepended to answer |
| 0.40 – 0.50 | **Medium** | LLM called, clean answer |
| ≥ 0.50 | **High** | LLM called, clean answer |

A second safety net catches cases where a borderline score passes the gate but the LLM itself signals it can't answer — the opening sentence of the response is checked for refusal patterns ("doesn't mention", "can't find", "isn't in the", etc.) and the response is reclassified as Insufficient.

**Every answer returns**:

```json
{
  "answer": "According to page 13, the mAP improved from 61.2% to 68.7%...",
  "confidence": 0.61,
  "confidenceLabel": "High",
  "evidence": [
    {
      "chunkText": "...",
      "page": 13,
      "section": "5.1 Performance Evaluation",
      "similarityScore": 0.61,
      "bm25Score": 0.0912,
      "hybridScore": 0.0163
    }
  ],
  "retrievalCount": 5,
  "rejectionReason": null
}
```

### USP 3 — Conversational Memory (Multi-Turn Chat)

Every query is sent with the last 3 turns of conversation history, enabling coherent follow-up questions without repeating context.

**How it works:**
- The client maintains the conversation thread and sends the full history with each request
- The backend caps history at 6 messages (3 turns) before passing to the LLM, keeping token cost bounded
- The LLM sees prior Q&A pairs between the system prompt and the current question, so "elaborate on that" or "what page was that?" resolves correctly
- No server-side session storage required — stateless design, no schema changes

**Token overhead:** ~200–400 additional tokens per follow-up. Bounded by the 3-turn cap.

---

### USP 4 — Document-Specific Query Suggestions

When a user opens a document, SmartDoc generates 6 questions tailored to that document's content and shows them as clickable chips — eliminating the cold-start problem of not knowing what to ask.

**How it works:**
1. After the document status resolves to `ready`, the frontend calls `GET /api/documents/{id}/suggestions`
2. The API samples 6 representative chunks from the document and sends them to Groq with a prompt to generate specific, answerable questions
3. Suggestions are shown immediately on the empty chat state — clicking one submits it instantly
4. If the Groq call fails for any reason, the UI silently falls back to 4 generic questions

**Token cost:** ~300 tokens per document visit (one-shot Groq call, not cached).

---

### USP 5 — Flashcard Generation with Zero Repeat Cost

After a document is ingested, SmartDoc can generate a study deck of 8–12 flashcards covering the key concepts in the document.

**How it works**:

1. **Evenly-sampled chunk retrieval** — instead of using the first N chunks (which skews toward the introduction) or all chunks (expensive), a SQL window function distributes the sample evenly across the document using a modulo step
2. **Structured LLM prompt** — the sampled chunks are sent to Groq with an explicit JSON schema instruction. The model returns a typed array of `{front, back, page, section}` objects, parsed and validated before being returned to the client
3. **DB-level caching** — generated cards are serialised and stored in the `flashcards_json` column. Subsequent requests return the cached result immediately — no LLM call, no tokens consumed
4. **Flip-card UI** — each card renders as a CSS 3D transform flip card with keyboard shortcuts: `Space` to flip, `←` / `→` to navigate

**Token cost**: first generation ~1,500 tokens. Every repeat view: 0 tokens.

---

### USP 6 — Semantic Document Summaries at Ingestion Time

When a document finishes indexing, SmartDoc immediately generates a 3–5 sentence prose summary and stores it alongside the document — so the library card tells you what a document is about before you open it.

**How it works:**

1. After chunks are embedded and saved, the ingestion pipeline samples 6 evenly-distributed chunks from the newly indexed document
2. A Groq call generates a concise prose summary: what the document covers, its key findings or purpose, and who it is for — no bullet points, no padding
3. The summary is persisted to a `summary TEXT` column on the `documents` table in a single `UPDATE`
4. Both `GET /api/documents` (library listing) and `GET /api/documents/{id}/status` return the summary field, so the frontend never needs a separate round-trip
5. Summary generation is wrapped in a non-fatal try/catch — if Groq is unavailable during ingestion, the document still becomes `ready` and the summary field is simply absent

**Result**: users see a plain-language description on every library card without any extra clicks.

**Token cost:** ~400–600 tokens per document, charged once at ingestion. Zero cost on subsequent views.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Browser (React + Tailwind)                   │
│                     Deployed on Vercel                           │
│  UploadPage ── drag-drop → status polling                       │
│  LibraryPage ── document management                             │
│  ChatPage ── Q&A with confidence meter + evidence cards         │
│  FlashcardsPage ── flip-card study deck (cached, 0 repeat cost) │
└──────────────┬──────────────────────────────────────────────────┘
               │ HTTP / REST
┌──────────────▼──────────────────────────────────────────────────┐
│              SmartDoc.Api  (ASP.NET Core 10, Docker)             │
│              Deployed on Railway                                  │
│                                                                  │
│  POST /api/documents/upload       GET /api/documents             │
│  GET  /api/documents/{id}/status  DELETE /api/documents/{id}     │
│  POST /api/documents/{id}/query   GET /api/documents/{id}/flashcards │
│                                                                  │
│  ┌──────────────────────────┐  ┌──────────────────────────────┐  │
│  │ PdfIngestionBackgroundSvc│  │ RetrievalService             │  │
│  │  1. PdfPig text extract  │  │  1. Embed query (Jina AI)    │  │
│  │  2. Type detection       │  │  2. Hybrid BM25+vector (RRF) │  │
│  │  3. Structure chunking   │  │  3. Confidence gate          │  │
│  │  4. TOC/junk filtering   │  │  4. LLM refusal detection    │  │
│  │  5. Overview chunk       │  │  5. Groq LLM (if gate passes)│  │
│  │  6. Jina embed + store   │  └──────────────────────────────┘  │
│  │  7. Groq summary + cache │                                    │
│  └──────────────────────────┘                                    │
└──────────────────────────────┬───────────────────────────────────┘
                               │ Npgsql + pgvector
┌──────────────────────────────▼───────────────────────────────────┐
│         PostgreSQL 16 + pgvector  (Render managed DB)            │
│  documents  — id, filename, status, doc_type, summary,           │
│               flashcards_json                                     │
│  chunks     — embedding vector(1024), search_vector tsvector,    │
│               section_name, page_number, chunk_index             │
└───────────────┬──────────────────────────────────────────────────┘
                │                              │
┌───────────────▼──────────────┐  ┌────────────▼───────────────────┐
│  Jina AI                      │  │  Groq API                      │
│  jina-embeddings-v3           │  │  llama-3.3-70b-versatile       │
│  1024-dim · free tier         │  │  Q&A + flashcard generation    │
└──────────────────────────────┘  └────────────────────────────────┘
```

---

## Technical Decisions

**Why ASP.NET Core Minimal API?**
Strongly typed, high-performance, and production-proven. Minimal API removes boilerplate while keeping full DI, middleware, and OpenAPI support. BackgroundService provides a clean async ingestion queue without a separate message broker.

**Why PostgreSQL + pgvector instead of a dedicated vector DB (Pinecone, Weaviate)?**
pgvector keeps the stack simple — one database handles relational document metadata, vector similarity search (ivfflat index), and BM25 full-text search (GIN index on `tsvector`). A dedicated vector DB would add operational complexity and lose the ability to do hybrid SQL + vector queries in a single round trip.

**Why Jina AI for embeddings?**
`jina-embeddings-v3` produces high-quality 1024-dim embeddings with a generous free tier (1M tokens). The `IEmbeddingService` interface keeps the embedding provider swappable — switching to OpenAI or Ollama is a one-line DI change with no business logic touched.

**Why Groq instead of OpenAI for the LLM?**
Groq's inference hardware delivers significantly lower latency on open-weight models. `llama-3.3-70b-versatile` on Groq produces answers in 1–2 seconds vs 4–6 seconds on comparable OpenAI endpoints, at a fraction of the cost.

**Why Reciprocal Rank Fusion instead of score normalisation?**
Score normalisation across BM25 and cosine similarity is fragile — the scales and distributions differ. RRF only requires rank positions, is parameter-light (one constant k=60), and consistently outperforms naive score blending in information retrieval benchmarks.

**Why Docker on Railway instead of a native runtime?**
ASP.NET Core 10 is not yet available as a native runtime on major PaaS providers. Docker gives full control over the runtime environment and makes the deployment reproducible regardless of the host.

---

## Key Results

Tested against a 24-page computer vision technical document:

| Metric | Result |
|---|---|
| Chunks produced (after filtering) | 22 clean chunks (vs 129 raw, 102 junk) |
| Chunks with section name assigned | >90% |
| In-scope questions answered correctly | High / Medium confidence |
| Out-of-scope questions correctly rejected | "Capital of France?" → Insufficient 28% |
| Repeat flashcard generation cost | 0 tokens (cached in DB after first generation) |
| Token cost per query | ~600–900 tokens |

---

## Running Locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node 18+](https://nodejs.org)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Jina AI API key — free at [jina.ai](https://jina.ai) (used for embeddings)
- Groq API key — free at [console.groq.com](https://console.groq.com) (used for LLM)

### 1. Start PostgreSQL + pgvector

```bash
cp .env.example .env
# Edit .env — set JINA_API_KEY, GROQ_API_KEY, and POSTGRES_PASSWORD

docker compose up -d
```

### 2. Run the API

```bash
bash run-api.sh
# API at http://localhost:5001  ·  Swagger UI at http://localhost:5001/swagger
```

### 3. Run the frontend

```bash
cd smartdoc-ui
npm install
npm run dev
# UI at http://localhost:5173
```

### 4. Run unit tests

```bash
cd SmartDoc.Tests && dotnet test
```

---

## Deployment

| Layer | Platform | Notes |
|---|---|---|
| Frontend | Vercel | Auto-deploys on push to `main` |
| API | Railway (Docker) | Auto-deploys on push to `main` |
| Database | Render managed PostgreSQL | pgvector extension enabled |

### Environment Variables

**Railway (API):**

| Variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | Render PostgreSQL connection string |
| `Jina__ApiKey` | Jina AI API key |
| `Groq__ApiKey` | Groq API key |
| `AllowedOrigins` | Comma-separated list of allowed frontend URLs |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `PORT` | Set to `8000` |

**Vercel (Frontend):**

| Variable | Description |
|---|---|
| `VITE_API_BASE_URL` | Railway API URL |

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 10 Minimal API, C# |
| Database | PostgreSQL 16 + pgvector extension |
| Embeddings | Jina AI · jina-embeddings-v3 (1024-dim) |
| LLM | Groq · llama-3.3-70b-versatile |
| PDF extraction | PdfPig |
| Validation | FluentValidation |
| Logging | Serilog (structured, rolling file) |
| Frontend | React 18, TypeScript, Tailwind CSS, Vite |
| Containerisation | Docker |
| API hosting | Railway |
| Frontend hosting | Vercel |
| Database hosting | Render |
