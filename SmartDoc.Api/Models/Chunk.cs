namespace SmartDoc.Api.Models;

public class Chunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionName { get; set; }
    public string ChunkType { get; set; } = ChunkTypes.Fallback;
    public int? CharStart { get; set; }
    public int? CharEnd { get; set; }
    public int ChunkIndex { get; set; }
}

public static class ChunkTypes
{
    public const string Structural = "structural";
    public const string Fallback = "fallback";
}

public record ScoredChunk(
    Chunk Chunk,
    double SimilarityScore,  // cosine similarity — used for confidence gate
    double BM25Score,        // ts_rank_cd keyword score
    double HybridScore       // RRF combined rank score — used for ordering
);
