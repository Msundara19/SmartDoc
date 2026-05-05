using System.Data;
using System.Data.Common;
using System.Text.Json;
using Npgsql;
using Pgvector;
using SmartDoc.Api.Models;

namespace SmartDoc.Api.Infrastructure;

public interface IVectorRepository
{
    Task<Guid> CreateDocumentAsync(Document document, CancellationToken ct = default);
    Task<Document?> GetDocumentAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct = default);
    Task UpdateDocumentStatusAsync(Guid id, string status, string? errorMessage = null, CancellationToken ct = default);
    Task UpdateDocumentMetadataAsync(Guid id, string docType, int pageCount, CancellationToken ct = default);
    Task SaveChunksAsync(IEnumerable<Chunk> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<ScoredChunk>> SearchHybridAsync(Guid documentId, float[] queryEmbedding, string queryText, int topK = 5, CancellationToken ct = default);
    Task<IReadOnlyList<Chunk>> GetDocumentChunksAsync(Guid documentId, int sampleCount = 8, CancellationToken ct = default);
    Task<List<Flashcard>?> GetCachedFlashcardsAsync(Guid documentId, CancellationToken ct = default);
    Task SaveFlashcardsAsync(Guid documentId, List<Flashcard> cards, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
}

public class VectorRepository : IVectorRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public VectorRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Guid> CreateDocumentAsync(Document document, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (id, filename, upload_time, status, file_size_bytes)
            VALUES (@id, @filename, @uploadTime, @status, @fileSizeBytes)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("id", document.Id);
        cmd.Parameters.AddWithValue("filename", document.Filename);
        cmd.Parameters.AddWithValue("uploadTime", document.UploadTime);
        cmd.Parameters.AddWithValue("status", document.Status);
        cmd.Parameters.AddWithValue("fileSizeBytes", (object?)document.FileSizeBytes ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<Document?> GetDocumentAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapDocument(reader);
    }

    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents ORDER BY upload_time DESC";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var docs = new List<Document>();
        while (await reader.ReadAsync(ct))
            docs.Add(MapDocument(reader));
        return docs;
    }

    public async Task UpdateDocumentStatusAsync(Guid id, string status, string? errorMessage = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET status = @status, error_message = @errorMessage WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("errorMessage", (object?)errorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateDocumentMetadataAsync(Guid id, string docType, int pageCount, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET doc_type = @docType, page_count = @pageCount WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("docType", docType);
        cmd.Parameters.AddWithValue("pageCount", pageCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveChunksAsync(IEnumerable<Chunk> chunks, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Batch insert using a transaction for performance
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var chunk in chunks)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO chunks
                    (id, document_id, chunk_text, embedding, page_number,
                     section_name, chunk_type, char_start, char_end, chunk_index)
                VALUES
                    (@id, @documentId, @chunkText, @embedding, @pageNumber,
                     @sectionName, @chunkType, @charStart, @charEnd, @chunkIndex)
                """;
            cmd.Parameters.AddWithValue("id", chunk.Id == Guid.Empty ? Guid.NewGuid() : chunk.Id);
            cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
            cmd.Parameters.AddWithValue("chunkText", chunk.ChunkText);
            cmd.Parameters.AddWithValue("embedding",
                chunk.Embedding != null ? new Vector(chunk.Embedding) : DBNull.Value);
            cmd.Parameters.AddWithValue("pageNumber", (object?)chunk.PageNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sectionName", (object?)chunk.SectionName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("chunkType", chunk.ChunkType);
            cmd.Parameters.AddWithValue("charStart", (object?)chunk.CharStart ?? DBNull.Value);
            cmd.Parameters.AddWithValue("charEnd", (object?)chunk.CharEnd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchHybridAsync(
        Guid documentId, float[] queryEmbedding, string queryText,
        int topK = 5, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Reciprocal Rank Fusion (RRF, k=60) combines vector and BM25 rankings
        // without requiring score normalisation. The confidence gate still uses
        // the raw cosine similarity so semantic relevance governs rejection.
        cmd.CommandText = """
            WITH query AS (
                SELECT plainto_tsquery('english', @queryText) AS tsq
            ),
            vector_ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (ORDER BY embedding <=> @queryEmbedding) AS vrank,
                       1 - (embedding <=> @queryEmbedding)                         AS vector_score
                FROM chunks
                WHERE document_id = @documentId
                  AND embedding IS NOT NULL
            ),
            bm25_ranked AS (
                SELECT c.id,
                       ROW_NUMBER() OVER (
                           ORDER BY ts_rank_cd(c.search_vector, q.tsq) DESC
                       )                                     AS brank,
                       ts_rank_cd(c.search_vector, q.tsq)   AS bm25_score
                FROM chunks c, query q
                WHERE c.document_id = @documentId
                  AND c.search_vector @@ q.tsq
            )
            SELECT c.id, c.document_id, c.chunk_text, c.page_number, c.section_name,
                   c.chunk_type, c.char_start, c.char_end, c.chunk_index,
                   vr.vector_score,
                   COALESCE(br.bm25_score, 0)                            AS bm25_score,
                   (1.0 / (60 + vr.vrank) + 1.0 / (60 + COALESCE(br.brank, 999))) AS rrf_score
            FROM chunks c
            JOIN vector_ranked vr ON c.id = vr.id
            LEFT JOIN bm25_ranked br ON c.id = br.id
            ORDER BY rrf_score DESC
            LIMIT @topK
            """;

        cmd.Parameters.AddWithValue("documentId", documentId);
        cmd.Parameters.AddWithValue("queryEmbedding", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("queryText", queryText);
        cmd.Parameters.AddWithValue("topK", topK);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ScoredChunk>();

        // Read ordinals once before the loop to avoid repeated lookups per row.
        var ordId           = reader.GetOrdinal("id");
        var ordDocumentId   = reader.GetOrdinal("document_id");
        var ordChunkText    = reader.GetOrdinal("chunk_text");
        var ordPageNumber   = reader.GetOrdinal("page_number");
        var ordSectionName  = reader.GetOrdinal("section_name");
        var ordChunkType    = reader.GetOrdinal("chunk_type");
        var ordCharStart    = reader.GetOrdinal("char_start");
        var ordCharEnd      = reader.GetOrdinal("char_end");
        var ordChunkIndex   = reader.GetOrdinal("chunk_index");
        var ordVectorScore  = reader.GetOrdinal("vector_score");
        var ordBm25Score    = reader.GetOrdinal("bm25_score");
        var ordRrfScore     = reader.GetOrdinal("rrf_score");

        while (await reader.ReadAsync(ct))
        {
            var chunk = new Chunk
            {
                Id = reader.GetGuid(ordId),
                DocumentId = reader.GetGuid(ordDocumentId),
                ChunkText = reader.GetString(ordChunkText),
                PageNumber = reader.IsDBNull(ordPageNumber) ? null : reader.GetInt32(ordPageNumber),
                SectionName = reader.IsDBNull(ordSectionName) ? null : reader.GetString(ordSectionName),
                ChunkType = reader.GetString(ordChunkType),
                CharStart = reader.IsDBNull(ordCharStart) ? null : reader.GetInt32(ordCharStart),
                CharEnd = reader.IsDBNull(ordCharEnd) ? null : reader.GetInt32(ordCharEnd),
                ChunkIndex = reader.GetInt32(ordChunkIndex)
            };
            results.Add(new ScoredChunk(
                chunk,
                SimilarityScore: reader.GetDouble(ordVectorScore),
                BM25Score:       reader.GetDouble(ordBm25Score),
                HybridScore:     reader.GetDouble(ordRrfScore)
            ));
        }
        return results;
    }

    public async Task<IReadOnlyList<Chunk>> GetDocumentChunksAsync(Guid documentId, int sampleCount = 8, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Evenly sample across the document: when there are more chunks than
        // sampleCount, pick every (total/sampleCount)-th chunk by position so
        // the selection spans the whole document rather than just the start.
        cmd.CommandText = """
            WITH numbered AS (
                SELECT id, document_id, chunk_text, page_number, section_name,
                       chunk_type, char_start, char_end, chunk_index,
                       ROW_NUMBER() OVER (ORDER BY chunk_index) - 1 AS rn,
                       COUNT(*)                               OVER () AS total
                FROM chunks
                WHERE document_id = @documentId
                  AND chunk_index >= 0
            )
            SELECT id, document_id, chunk_text, page_number, section_name,
                   chunk_type, char_start, char_end, chunk_index
            FROM numbered
            WHERE total <= @sampleCount
               OR rn % GREATEST(total / @sampleCount, 1) = 0
            ORDER BY chunk_index
            LIMIT @sampleCount
            """;
        cmd.Parameters.AddWithValue("documentId", documentId);
        cmd.Parameters.AddWithValue("sampleCount", sampleCount);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var chunks = new List<Chunk>();

        var ordId          = reader.GetOrdinal("id");
        var ordDocumentId  = reader.GetOrdinal("document_id");
        var ordChunkText   = reader.GetOrdinal("chunk_text");
        var ordPageNumber  = reader.GetOrdinal("page_number");
        var ordSectionName = reader.GetOrdinal("section_name");
        var ordChunkType   = reader.GetOrdinal("chunk_type");
        var ordCharStart   = reader.GetOrdinal("char_start");
        var ordCharEnd     = reader.GetOrdinal("char_end");
        var ordChunkIndex  = reader.GetOrdinal("chunk_index");

        while (await reader.ReadAsync(ct))
        {
            chunks.Add(new Chunk
            {
                Id          = reader.GetGuid(ordId),
                DocumentId  = reader.GetGuid(ordDocumentId),
                ChunkText   = reader.GetString(ordChunkText),
                PageNumber  = reader.IsDBNull(ordPageNumber)  ? null : reader.GetInt32(ordPageNumber),
                SectionName = reader.IsDBNull(ordSectionName) ? null : reader.GetString(ordSectionName),
                ChunkType   = reader.GetString(ordChunkType),
                CharStart   = reader.IsDBNull(ordCharStart)   ? null : reader.GetInt32(ordCharStart),
                CharEnd     = reader.IsDBNull(ordCharEnd)     ? null : reader.GetInt32(ordCharEnd),
                ChunkIndex  = reader.GetInt32(ordChunkIndex)
            });
        }
        return chunks;
    }

    public async Task<List<Flashcard>?> GetCachedFlashcardsAsync(Guid documentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT flashcards_json FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("id", documentId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        var json = (string)result;
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<List<Flashcard>>(json);
    }

    public async Task SaveFlashcardsAsync(Guid documentId, List<Flashcard> cards, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET flashcards_json = @json WHERE id = @id";
        cmd.Parameters.AddWithValue("id", documentId);
        cmd.Parameters.AddWithValue("json", JsonSerializer.Serialize(cards));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // CASCADE on chunks table handles chunk deletion
        cmd.CommandText = "DELETE FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Document MapDocument(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        Filename = reader.GetString(reader.GetOrdinal("filename")),
        UploadTime = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("upload_time")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        DocType = reader.IsDBNull(reader.GetOrdinal("doc_type")) ? null : reader.GetString(reader.GetOrdinal("doc_type")),
        PageCount = reader.IsDBNull(reader.GetOrdinal("page_count")) ? null : reader.GetInt32(reader.GetOrdinal("page_count")),
        ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
        FileSizeBytes = reader.IsDBNull(reader.GetOrdinal("file_size_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("file_size_bytes"))
    };
}
