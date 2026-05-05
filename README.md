# SmartDoc — Intelligent PDF Q&A

> A production-grade Retrieval-Augmented Generation (RAG) system that answers questions about PDF documents with **verifiable confidence scores** and **zero hallucination** on out-of-scope queries.

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
- **Ingestion pipeline** extracts text, detects document structure, chunks intelligently, embeds locally, and indexes into a vector + full-text search database
- **Q&A** with confidence scoring — the system tells you how reliable each answer is, shows you the source chunks it used, and refuses to answer questions the document can't support
- **Flashcard generation** — automatically extracts key concepts from the document into study cards, cached after first generation
- **Library** to manage multiple documents

---

## The Two USPs

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

**Confidence gate** uses the raw cosine similarity of the top-ranked chunk (not the fused score) to decide whether to call the LLM at all:

| Score | Label | Behaviour |
|---|---|---|
| < 0.42 | **Insufficient** | LLM never called — `rejectionReason` returned instead |
| 0.42 – 0.55 | **Low** | LLM called, disclaimer prepended to answer |
| 0.55 – 0.63 | **Medium** | LLM called, clean answer |
| ≥ 0.63 | **High** | LLM called, clean answer |

A second safety net catches cases where a borderline score passes the gate but the LLM itself signals it can't answer — the opening sentence of the response is checked for refusal patterns ("doesn't mention", "can't find", "isn't in the", etc.) and the response is reclassified as Insufficient.

**Every answer returns**:


```json
{
  "answer": "According to page 13, the mAP improved from 61.2% to 68.7%...",
  "confidence": 0.6841,
  "confidenceLabel": "High",
  "evidence": [
    {
      "chunkText": "...",
      "page": 13,
      "section": "5.1 Performance Evaluation",
      "similarityScore": 0.6841,
      "bm25Score": 0.0912,
      "hybridScore": 0.0163
    }
  ],
  "retrievalCount": 5,
  "rejectionReason": null
}
```

### USP 3 — Flashcard Generation with Zero Repeat Cost

After a document is ingested, SmartDoc can generate a study deck of 8–12 flashcards covering the key concepts in the document.

**How it works**:

1. **Evenly-sampled chunk retrieval** — instead of using the first N chunks (which skews toward the introduction) or all chunks (expensive), a SQL window function distributes the sample evenly across the document using a modulo step:
   ```sql
   WHERE total <= @sampleCount OR rn % GREATEST(total / @sampleCount, 1) = 0
   ```
   This ensures a 60-page document and a 6-page document both produce representative flashcards.

2. **Structured LLM prompt** — the sampled chunks are sent to Groq with an explicit JSON schema instruction. The model returns a typed array of `{front, back, page, section}` objects, which are parsed and validated before being returned to the client.

3. **DB-level caching** — generated cards are serialised and stored in the `flashcards_json` column on the `documents` row. Subsequent requests return the cached result immediately — no LLM call, no tokens consumed. A `?refresh=true` query parameter bypasses the cache when needed.

4. **Flip-card UI** — each card renders as a CSS 3D transform flip card. Clicking flips the card; keyboard shortcuts work throughout: `Space` to flip, `←` / `→` to navigate. A dot progress bar across the top gives position at a glance.

**Token cost**: first generation ~1,500 tokens. Every repeat view: 0 tokens.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Browser (React + Tailwind)                   │
│  UploadPage ── drag-drop → status polling                       │
│  LibraryPage ── document management                             │
│  ChatPage ── Q&A with confidence meter + evidence cards         │
│  FlashcardsPage ── flip-card study deck (cached, 0 repeat cost) │
└──────────────┬──────────────────────────────────┬───────────────┘
               │ HTTP / REST                       │
┌──────────────▼──────────────────────────────────▼───────────────┐
│                   SmartDoc.Api  (ASP.NET Core 8)                 │
│                                                                  │
│  POST /api/documents/upload       GET /api/documents             │
│  GET  /api/documents/{id}/status  DELETE /api/documents/{id}     │
│  POST /api/documents/{id}/query   GET /api/documents/{id}/flashcards │
│                                                                  │
│  ┌──────────────────────────┐  ┌──────────────────────────────┐  │
│  │ PdfIngestionBackgroundSvc│  │ RetrievalService             │  │
│  │  1. PdfPig text extract  │  │  1. Embed query (OpenAI)     │  │
│  │  2. Type detection       │  │  2. Hybrid BM25+vector (RRF) │  │
│  │  3. Structure chunking   │  │  3. Confidence gate          │  │
│  │  4. TOC/junk filtering   │  │  4. LLM refusal detection    │  │
│  │  5. Overview chunk       │  │  5. Groq LLM (if gate passes)│  │
│  │  6. OpenAI embed + store │  └──────────────────────────────┘  │
│  └──────────────────────────┘                                    │
└──────────────────────────────┬───────────────────────────────────┘
                               │ Npgsql + pgvector
┌──────────────────────────────▼───────────────────────────────────┐
│              PostgreSQL 16 + pgvector  (Docker / Render)          │
│  documents  — id, filename, status, doc_type, flashcards_json    │
│  chunks     — embedding vector(1536), search_vector tsvector,    │
│               section_name, page_number, chunk_index             │
└───────────────┬──────────────────────────────────────────────────┘
                │                              │
┌───────────────▼──────────────┐  ┌────────────▼───────────────────┐
│  OpenAI API                   │  │  Groq API                      │
│  text-embedding-3-small       │  │  llama-3.3-70b-versatile       │
│  1536-dim                     │  │  Q&A + flashcard generation    │
└──────────────────────────────┘  └────────────────────────────────┘
```

---

## Technical Decisions

**Why ASP.NET Core 8 Minimal API?**
Strongly typed, high-performance, and production-proven. Minimal API removes boilerplate while keeping full DI, middleware, and OpenAPI support. BackgroundService provides a clean async ingestion queue without a separate message broker.

**Why PostgreSQL + pgvector instead of a dedicated vector DB (Pinecone, Weaviate)?**
pgvector keeps the stack simple — one database handles relational document metadata, vector similarity search (ivfflat index), and BM25 full-text search (GIN index on `tsvector`). A dedicated vector DB would add operational complexity and lose the ability to do hybrid SQL + vector queries in a single round trip.

**Why OpenAI text-embedding-3-small for embeddings?**
High-quality 1536-dim embeddings via a single API call, with no infrastructure to run. For local development, the `IEmbeddingService` interface makes it a one-class swap to Ollama `nomic-embed-text` (768-dim) — the only schema change is `vector(1536)` → `vector(768)`.

**Why Groq instead of OpenAI for the LLM?**
Groq's inference hardware delivers significantly lower latency on open-weight models. `llama-3.3-70b-versatile` on Groq produces answers in 1–2 seconds vs 4–6 seconds on comparable OpenAI endpoints, at a fraction of the cost.

**Why Reciprocal Rank Fusion instead of score normalisation?**
Score normalisation across BM25 and cosine similarity is fragile — the scales and distributions differ. RRF only requires rank positions, is parameter-light (one constant k=60), and consistently outperforms naive score blending in information retrieval benchmarks.

---

## Key Results

Tested against a 24-page computer vision technical document:

| Metric | Result |
|---|---|
| Chunks produced (after filtering) | 22 clean chunks (vs 129 raw, 102 junk) |
| Chunks with section name assigned | >90% |
| In-scope questions answered correctly | High / Medium confidence |
| Out-of-scope questions correctly rejected | "Capital of France?" → Insufficient 38% |
| Repeat flashcard generation cost | 0 tokens (cached in DB after first generation) |
| Token cost per query after optimisation | ~600–900 tokens (vs ~2,000 baseline) |

---

## Running Locally

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node 18+](https://nodejs.org)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- OpenAI API key — [platform.openai.com](https://platform.openai.com) (used for embeddings)
- Groq API key — free tier at [console.groq.com](https://console.groq.com) (used for LLM)

### 1. Start PostgreSQL + pgvector

```bash
cp .env.example .env
# Edit .env — set OPENAI_API_KEY, GROQ_API_KEY, and POSTGRES_PASSWORD

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

### Backend (Render Web Service)

1. Build: `cd SmartDoc.Api && dotnet publish -c Release -o out`
2. Start: `cd SmartDoc.Api/out && dotnet SmartDoc.Api.dll`
3. Env vars: `ConnectionStrings__DefaultConnection`, `Groq__ApiKey`, `OpenAI__ApiKey`
4. Add a Render PostgreSQL instance — install pgvector via the shell: `CREATE EXTENSION IF NOT EXISTS vector;`
5. Swap embeddings for cloud: in `Program.cs` change `OllamaEmbeddingService` → `OpenAiEmbeddingService`, update schema column to `vector(1536)`

### Frontend (Render Static Site)

1. Root: `smartdoc-ui` · Build: `npm install && npm run build` · Publish: `dist`
2. Env var: `VITE_API_BASE_URL=https://your-backend.onrender.com`

### Railway (alternative)

Deploy `docker-compose.yml` directly — Railway supports Compose deployments out of the box.

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 8 Minimal API, C# |
| Database | PostgreSQL 16 + pgvector extension |
| Embeddings | OpenAI · text-embedding-3-small (1536-dim) |
| LLM | Groq · llama-3.3-70b-versatile |
| PDF extraction | PdfPig |
| Validation | FluentValidation |
| Logging | Serilog (structured, rolling file) |
| Frontend | React 18, TypeScript, Tailwind CSS, Vite |
| Containerisation | Docker Compose |
