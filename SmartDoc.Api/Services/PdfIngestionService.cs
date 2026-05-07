using System.Collections.Concurrent;
using SmartDoc.Api.Infrastructure;
using SmartDoc.Api.Models;

namespace SmartDoc.Api.Services;

/// <summary>
/// In-process queue that feeds the background ingestion worker.
/// </summary>
public interface IIngestionQueue
{
    void Enqueue(IngestionJob job);
    bool TryDequeue(out IngestionJob? job);
}

public class IngestionQueue : IIngestionQueue
{
    private readonly ConcurrentQueue<IngestionJob> _queue = new();

    public void Enqueue(IngestionJob job) => _queue.Enqueue(job);
    public bool TryDequeue(out IngestionJob? job) => _queue.TryDequeue(out job);
}

/// <summary>
/// Background service that processes ingestion jobs from the queue.
/// Runs on a 500ms polling loop; each job embeds chunks and persists them.
/// </summary>
public class PdfIngestionBackgroundService : BackgroundService
{
    private readonly IIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PdfIngestionBackgroundService> _logger;

    public PdfIngestionBackgroundService(
        IIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<PdfIngestionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PDF ingestion background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var job) && job != null)
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            else
            {
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(IngestionJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IVectorRepository>();
        var chunker = scope.ServiceProvider.GetRequiredService<IChunkingService>();
        var embedder = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlmService>();

        _logger.LogInformation("Processing ingestion job for document {DocumentId}", job.DocumentId);

        try
        {
            await repo.UpdateDocumentStatusAsync(job.DocumentId, DocumentStatus.Processing, ct: ct);

            // Read PDF bytes from temp file; file is deleted in the finally block.
            var pdfBytes = await File.ReadAllBytesAsync(job.TempFilePath, ct);

            // Structure-aware chunking (USP 1)
            var (docType, chunks, pageCount) = chunker.ChunkDocument(job.DocumentId, pdfBytes);

            _logger.LogInformation(
                "Document {DocumentId} detected as {DocType}, produced {ChunkCount} chunks across {PageCount} pages.",
                job.DocumentId, docType, chunks.Count, pageCount);

            await repo.UpdateDocumentMetadataAsync(job.DocumentId, docType, pageCount, ct);

            // Embed all chunks in batch
            var texts = chunks.Select(c => c.ChunkText).ToList();
            var embeddings = await embedder.EmbedBatchAsync(texts, ct);

            if (embeddings.Count != chunks.Count)
                throw new InvalidOperationException(
                    $"Embedding count mismatch: expected {chunks.Count} but got {embeddings.Count}. " +
                    "The embedding service may have returned a partial response.");

            for (int i = 0; i < chunks.Count; i++)
                chunks[i].Embedding = embeddings[i];

            await repo.SaveChunksAsync(chunks, ct);

            // Generate and persist summary — fire-and-forget style; failure is non-fatal
            try
            {
                var sampleChunks = await repo.GetDocumentChunksAsync(job.DocumentId, sampleCount: 6, ct);
                var summary = await llm.GenerateSummaryAsync(sampleChunks, ct);
                if (!string.IsNullOrWhiteSpace(summary))
                    await repo.SaveSummaryAsync(job.DocumentId, summary, ct);
            }
            catch (Exception summaryEx)
            {
                _logger.LogWarning(summaryEx, "Summary generation failed for document {DocumentId}; continuing.", job.DocumentId);
            }

            await repo.UpdateDocumentStatusAsync(job.DocumentId, DocumentStatus.Ready, ct: ct);

            _logger.LogInformation("Document {DocumentId} ingestion complete.", job.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for document {DocumentId}", job.DocumentId);
            try
            {
                await repo.UpdateDocumentStatusAsync(
                    job.DocumentId, DocumentStatus.Failed,
                    errorMessage: ex.Message, ct: ct);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to persist error status for document {DocumentId}", job.DocumentId);
            }
        }
        finally
        {
            // Delete the temp file regardless of success or failure to free disk space.
            try
            {
                if (File.Exists(job.TempFilePath))
                    File.Delete(job.TempFilePath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to delete temp file {TempFilePath} for document {DocumentId}.",
                    job.TempFilePath, job.DocumentId);
            }
        }
    }
}
