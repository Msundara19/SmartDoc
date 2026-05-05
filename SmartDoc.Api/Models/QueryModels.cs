namespace SmartDoc.Api.Models;

// Using a class instead of a positional record so that a missing JSON
// "question" field deserialises to an empty string rather than null,
// preventing NullReferenceExceptions in the validator length checks.
public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
}

public class QueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string ConfidenceLabel { get; set; } = string.Empty;
    public List<EvidenceItem> Evidence { get; set; } = [];
    public int RetrievalCount { get; set; }
    public string? RejectionReason { get; set; }
}

public class EvidenceItem
{
    public string ChunkText { get; set; } = string.Empty;
    public int? Page { get; set; }
    public string? Section { get; set; }
    public double SimilarityScore { get; set; }
    public double BM25Score { get; set; }
    public double HybridScore { get; set; }
}
