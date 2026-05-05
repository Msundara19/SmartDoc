-- SmartDoc database schema
-- Run automatically by Docker Compose on first startup

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    filename        TEXT NOT NULL,
    upload_time     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status          TEXT NOT NULL DEFAULT 'pending',   -- pending | processing | ready | failed
    doc_type        TEXT,                              -- research_paper | legal | general | technical
    page_count      INT,
    error_message   TEXT,
    file_size_bytes BIGINT,
    flashcards_json TEXT                               -- cached JSON array of generated flashcards
);

CREATE TABLE IF NOT EXISTS chunks (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id     UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    chunk_text      TEXT NOT NULL,
    embedding       vector(1536),
    page_number     INT,
    section_name    TEXT,
    chunk_type      TEXT NOT NULL DEFAULT 'fallback',  -- structural | fallback
    char_start      INT,
    char_end        INT,
    chunk_index     INT NOT NULL DEFAULT 0,
    -- Auto-maintained full-text search vector for BM25 hybrid retrieval
    search_vector   tsvector GENERATED ALWAYS AS (
                        to_tsvector('english', coalesce(chunk_text, ''))
                    ) STORED
);

-- Cosine similarity index (ivfflat — fast approximate search)
CREATE INDEX IF NOT EXISTS chunks_embedding_idx
    ON chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- BM25 full-text search index
CREATE INDEX IF NOT EXISTS chunks_search_vector_idx
    ON chunks USING gin(search_vector);

-- Lookup indexes
CREATE INDEX IF NOT EXISTS chunks_document_id_idx ON chunks(document_id);
CREATE INDEX IF NOT EXISTS documents_status_idx ON documents(status);
