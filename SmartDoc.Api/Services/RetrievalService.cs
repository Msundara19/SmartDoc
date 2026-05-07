using SmartDoc.Api.Infrastructure;
using SmartDoc.Api.Models;

namespace SmartDoc.Api.Services;

public interface IRetrievalService
{
    Task<QueryResponse> QueryAsync(Guid documentId, string question,
        IEnumerable<ConversationMessage>? history = null, CancellationToken ct = default);
}

public class RetrievalService : IRetrievalService
{
    // Thresholds tuned for nomic-embed-text (local Ollama model).
    // Jina jina-embeddings-v3 cosine scores cluster lower than OpenAI/nomic.
    // LLM refusal detection acts as second-line defense for out-of-scope pass-throughs.
    private const double RejectionThreshold = 0.30;
    private const double LowThreshold = 0.40;
    private const double MediumThreshold = 0.50;
    private const int TopK = 5;

    private readonly IEmbeddingService _embedding;
    private readonly IVectorRepository _repository;
    private readonly ILlmService _llm;

    public RetrievalService(
        IEmbeddingService embedding,
        IVectorRepository repository,
        ILlmService llm)
    {
        _embedding = embedding;
        _repository = repository;
        _llm = llm;
    }

    public async Task<QueryResponse> QueryAsync(Guid documentId, string question,
        IEnumerable<ConversationMessage>? history = null, CancellationToken ct = default)
    {
        // Step 1: embed the query
        var queryEmbedding = await _embedding.EmbedAsync(question, ct);

        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new InvalidOperationException("Embedding service returned an empty vector.");

        // Step 2: hybrid BM25 + vector search via RRF
        var scoredChunks = await _repository.SearchHybridAsync(documentId, queryEmbedding, question, TopK, ct);

        if (scoredChunks.Count == 0)
        {
            return new QueryResponse
            {
                Answer = string.Empty,
                Confidence = 0,
                ConfidenceLabel = "Insufficient",
                RetrievalCount = 0,
                RejectionReason = "No content has been indexed for this document yet. " +
                    "Please wait for ingestion to complete."
            };
        }

        // After BM25-noise fix, RRF top-1 == cosine top-1 when BM25=0, so position 0 is safe.
        double topScore = scoredChunks[0].SimilarityScore;

        // Step 3: confidence gate
        if (topScore < RejectionThreshold)
        {
            return new QueryResponse
            {
                Answer = string.Empty,
                Confidence = topScore,
                ConfidenceLabel = "Insufficient",
                Evidence = MapEvidence(scoredChunks),
                RetrievalCount = scoredChunks.Count,
                RejectionReason = BuildRejectionReason(topScore, question)
            };
        }

        string confidenceLabel = topScore switch
        {
            >= MediumThreshold => "High",
            >= LowThreshold => "Medium",
            _ => "Low"
        };

        // Step 4: call LLM with context
        var answer = await _llm.GenerateAnswerAsync(question, scoredChunks, history, ct);

        // If the LLM itself signals the context is insufficient, treat as a rejection
        // rather than showing a misleading confidence label.
        if (IsLlmRefusal(answer))
        {
            return new QueryResponse
            {
                Answer = string.Empty,
                Confidence = Math.Round(topScore, 4),
                ConfidenceLabel = "Insufficient",
                Evidence = MapEvidence(scoredChunks),
                RetrievalCount = scoredChunks.Count,
                RejectionReason = $"The document does not appear to contain information relevant to \"{question}\". " +
                    "The retrieved passages did not provide enough context for a reliable answer."
            };
        }

        if (confidenceLabel == "Low")
            answer = "[Low confidence — treat this answer with caution]\n\n" + answer;

        return new QueryResponse
        {
            Answer = answer,
            Confidence = Math.Round(topScore, 4),
            ConfidenceLabel = confidenceLabel,
            Evidence = MapEvidence(scoredChunks),
            RetrievalCount = scoredChunks.Count,
            RejectionReason = null
        };
    }

    private static List<EvidenceItem> MapEvidence(IReadOnlyList<ScoredChunk> chunks) =>
        chunks.Select(sc => new EvidenceItem
        {
            ChunkText = sc.Chunk.ChunkText.Length > 500
                ? sc.Chunk.ChunkText[..500] + "…"
                : sc.Chunk.ChunkText,
            Page = sc.Chunk.PageNumber,
            Section = sc.Chunk.SectionName,
            SimilarityScore = Math.Round(sc.SimilarityScore, 4),
            BM25Score = Math.Round(sc.BM25Score, 6),
            HybridScore = Math.Round(sc.HybridScore, 6)
        }).ToList();

    // Detect refusals by inspecting only the opening sentence (first 160 chars).
    // Long answers that hedge mid-text ("...though the context doesn't fully cover X...")
    // are not refusals — they contain real content before the hedge.
    private static bool IsLlmRefusal(string answer)
    {
        var lead = answer.ToLowerInvariant();
        if (lead.Length > 160) lead = lead[..160];
        return lead.Contains("does not mention") ||
               lead.Contains("context provided does not") ||
               lead.Contains("cannot find any information") ||
               lead.Contains("there is no information about") ||
               lead.Contains("not in the provided context") ||
               lead.Contains("no information about") ||
               lead.Contains("context does not contain information") ||
               lead.Contains("doesn't mention") ||
               lead.Contains("don't have information") ||
               lead.Contains("isn't mentioned") ||
               lead.Contains("aren't mentioned") ||
               lead.Contains("can't find") ||
               lead.Contains("couldn't find") ||
               lead.Contains("isn't in the") ||
               lead.Contains("not mentioned in");
    }

    private static string BuildRejectionReason(double score, string question)
    {
        return $"The document does not appear to contain information relevant to \"{question}\". " +
               $"The best match found had a similarity score of {score:F2}, which is below the " +
               $"minimum threshold of {RejectionThreshold:F2} required to generate a reliable answer. " +
               "This prevents hallucinated responses for out-of-scope questions.";
    }
}
