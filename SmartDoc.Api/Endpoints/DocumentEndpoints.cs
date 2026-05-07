using FluentValidation;
using SmartDoc.Api.Infrastructure;
using SmartDoc.Api.Models;
using SmartDoc.Api.Services;

namespace SmartDoc.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        group.MapPost("/upload", UploadDocument);
        group.MapGet("/{id:guid}/status", GetDocumentStatus);
        group.MapGet("/", ListDocuments);
        group.MapPost("/{id:guid}/query", QueryDocument);
        group.MapGet("/{id:guid}/flashcards", GetFlashcards);
        group.MapGet("/{id:guid}/suggestions", GetSuggestions);
        group.MapDelete("/{id:guid}", DeleteDocument);
    }

    // POST /api/documents/upload
    private static async Task<IResult> UploadDocument(
        HttpRequest request,
        IIngestionQueue queue,
        IVectorRepository repo,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return Results.Problem("Request must be multipart/form-data.", statusCode: 422);

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read multipart form data.");
            return Results.Problem("Failed to read the uploaded form data. The request may have been interrupted.", statusCode: 400);
        }

        var file = form.Files.GetFile("file");

        if (file == null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A PDF file is required."]
            });

        // Guard against empty files
        if (file.Length == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["The uploaded file is empty."]
            });

        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            && !(file.FileName ?? string.Empty).EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["Only PDF files are accepted."]
            });
        }

        if (file.Length > 50 * 1024 * 1024) // 50 MB limit
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["File size must not exceed 50 MB."]
            });
        }

        var tempFilePath = Path.GetTempFileName();
        try
        {
            await using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(fs, ct);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read uploaded file bytes.");
            try { File.Delete(tempFilePath); } catch { /* best-effort cleanup */ }
            return Results.Problem("Failed to read the uploaded file. The request may have been interrupted.", statusCode: 400);
        }

        // Sanitise the filename: strip directory traversal and fall back if blank
        var rawFilename = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(rawFilename))
            rawFilename = "upload.pdf";

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Filename = rawFilename,
            UploadTime = DateTimeOffset.UtcNow,
            Status = DocumentStatus.Pending,
            FileSizeBytes = file.Length
        };

        try
        {
            await repo.CreateDocumentAsync(doc, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist document record for {Filename}.", doc.Filename);
            return Results.Problem("Failed to save the document record. Please try again.", statusCode: 503);
        }

        queue.Enqueue(new IngestionJob
        {
            DocumentId = doc.Id,
            TempFilePath = tempFilePath,
            Filename = doc.Filename
        });

        logger.LogInformation("Document {DocumentId} queued for ingestion.", doc.Id);

        return Results.Accepted($"/api/documents/{doc.Id}/status", new
        {
            documentId = doc.Id,
            filename = doc.Filename,
            status = doc.Status,
            message = "Document accepted for processing. Poll the status endpoint for progress."
        });
    }

    // GET /api/documents/{id}/status
    private static async Task<IResult> GetDocumentStatus(
        Guid id,
        IVectorRepository repo,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        Document? doc;
        try
        {
            doc = await repo.GetDocumentAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve status for document {DocumentId}.", id);
            return Results.Problem("Failed to retrieve document status. Please try again.", statusCode: 503);
        }

        if (doc == null) return Results.NotFound(new { error = $"Document {id} not found." });

        return Results.Ok(new
        {
            documentId = doc.Id,
            filename = doc.Filename,
            status = doc.Status,
            docType = doc.DocType,
            pageCount = doc.PageCount,
            uploadTime = doc.UploadTime,
            errorMessage = doc.ErrorMessage,
            summary = doc.Summary
        });
    }

    // GET /api/documents
    private static async Task<IResult> ListDocuments(
        IVectorRepository repo,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        IReadOnlyList<Document> docs;
        try
        {
            docs = await repo.ListDocumentsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list documents.");
            return Results.Problem("Failed to retrieve documents. Please try again.", statusCode: 503);
        }

        return Results.Ok(docs.Select(d => new
        {
            documentId = d.Id,
            filename = d.Filename,
            status = d.Status,
            docType = d.DocType,
            pageCount = d.PageCount,
            uploadTime = d.UploadTime,
            fileSizeBytes = d.FileSizeBytes,
            summary = d.Summary
        }));
    }

    // POST /api/documents/{id}/query
    private static async Task<IResult> QueryDocument(
        Guid id,
        QueryRequest? body,
        IVectorRepository repo,
        IRetrievalService retrieval,
        IValidator<QueryRequest> validator,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        // Guard against a missing or null request body (wrong Content-Type, empty body, etc.)
        if (body is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["A JSON request body with a 'question' field is required."]
            });
        }

        var validation = await validator.ValidateAsync(body, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            );
        }

        // Trim here so the question sent to the LLM and stored in logs is clean.
        // The validator already confirmed non-empty non-whitespace, so Trim() is safe.
        var question = body.Question.Trim();

        Document? doc;
        try
        {
            doc = await repo.GetDocumentAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve document {DocumentId} for query.", id);
            return Results.Problem("Failed to retrieve the document. Please try again.", statusCode: 503);
        }

        if (doc == null) return Results.NotFound(new { error = $"Document {id} not found." });

        if (doc.Status == DocumentStatus.Processing || doc.Status == DocumentStatus.Pending)
        {
            return Results.Problem(
                $"Document is still being processed (status: {doc.Status}). Please wait for ingestion to complete before querying.",
                statusCode: 409);
        }

        if (doc.Status == DocumentStatus.Failed)
        {
            return Results.Problem(
                $"Document ingestion failed and this document cannot be queried. Error: {doc.ErrorMessage ?? "Unknown error."}",
                statusCode: 422);
        }

        if (doc.Status != DocumentStatus.Ready)
        {
            return Results.Problem(
                $"Document is not ready for queries. Current status: {doc.Status}.",
                statusCode: 409);
        }

        try
        {
            var response = await retrieval.QueryAsync(id, question, body.History, ct);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("rate-limited"))
        {
            return Results.Problem(ex.Message, statusCode: 503);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Downstream service unreachable during query for document {DocumentId}.", id);
            return Results.Problem(
                "A required AI service (embedding or LLM) is currently unreachable. Please ensure the service is running and try again.",
                statusCode: 503);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Downstream service timed out during query for document {DocumentId}.", id);
            return Results.Problem(
                "The request timed out while waiting for the AI service. Please try again.",
                statusCode: 504);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during query for document {DocumentId}.", id);
            return Results.Problem(
                "An unexpected error occurred while processing your query. Please try again.",
                statusCode: 500);
        }
    }

    // GET /api/documents/{id}/flashcards?refresh=true
    private static async Task<IResult> GetFlashcards(
        Guid id,
        IVectorRepository repo,
        ILlmService llm,
        ILogger<WebApplication> logger,
        CancellationToken ct,
        bool refresh = false)
    {
        Document? doc;
        try
        {
            doc = await repo.GetDocumentAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve document {DocumentId} for flashcards.", id);
            return Results.Problem("Failed to retrieve the document. Please try again.", statusCode: 503);
        }

        if (doc == null) return Results.NotFound(new { error = $"Document {id} not found." });

        if (doc.Status != DocumentStatus.Ready)
            return Results.Problem(
                $"Document is not ready (status: {doc.Status}). Flashcards require an indexed document.",
                statusCode: 409);

        // Return cached flashcards on repeat visits — zero LLM tokens consumed.
        // Pass ?refresh=true to force regeneration (e.g. after re-indexing).
        if (!refresh)
        {
            try
            {
                var cached = await repo.GetCachedFlashcardsAsync(id, ct);
                if (cached is { Count: > 0 })
                {
                    logger.LogInformation("Returning cached flashcards for document {DocumentId}.", id);
                    return Results.Ok(new FlashcardsResponse { Cards = cached, ChunksUsed = 0 });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read flashcard cache for {DocumentId}; will regenerate.", id);
            }
        }

        IReadOnlyList<Chunk> chunks;
        try
        {
            chunks = await repo.GetDocumentChunksAsync(id, sampleCount: 8, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve chunks for document {DocumentId}.", id);
            return Results.Problem("Failed to retrieve document content. Please try again.", statusCode: 503);
        }

        if (chunks.Count == 0)
            return Results.Problem("No indexed content found for this document.", statusCode: 404);

        try
        {
            var cards = await llm.GenerateFlashcardsAsync(chunks, ct);

            // Persist to cache — fire-and-forget; a failure here is non-fatal
            _ = repo.SaveFlashcardsAsync(id, cards, CancellationToken.None);

            return Results.Ok(new FlashcardsResponse { Cards = cards, ChunksUsed = chunks.Count });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("rate-limited"))
        {
            return Results.Problem(ex.Message, statusCode: 503);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Downstream service unreachable during flashcard generation for {DocumentId}.", id);
            return Results.Problem(
                "A required AI service is currently unreachable. Please try again.", statusCode: 503);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Timeout during flashcard generation for {DocumentId}.", id);
            return Results.Problem("The request timed out. Please try again.", statusCode: 504);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during flashcard generation for {DocumentId}.", id);
            return Results.Problem(
                "An unexpected error occurred generating flashcards. Please try again.", statusCode: 500);
        }
    }

    // GET /api/documents/{id}/suggestions
    private static async Task<IResult> GetSuggestions(
        Guid id,
        IVectorRepository repo,
        ILlmService llm,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        var doc = await repo.GetDocumentAsync(id, ct);
        if (doc == null) return Results.NotFound(new { error = $"Document {id} not found." });
        if (doc.Status != DocumentStatus.Ready)
            return Results.Problem($"Document is not ready (status: {doc.Status}).", statusCode: 409);

        var chunks = await repo.GetDocumentChunksAsync(id, sampleCount: 6, ct);
        if (chunks.Count == 0) return Results.Ok(new { suggestions = Array.Empty<string>() });

        try
        {
            var suggestions = await llm.GenerateSuggestionsAsync(chunks, ct);
            return Results.Ok(new { suggestions });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate suggestions for document {DocumentId}.", id);
            return Results.Ok(new { suggestions = Array.Empty<string>() });
        }
    }

    // DELETE /api/documents/{id}
    private static async Task<IResult> DeleteDocument(
        Guid id,
        IVectorRepository repo,
        ILogger<WebApplication> logger,
        CancellationToken ct)
    {
        Document? doc;
        try
        {
            doc = await repo.GetDocumentAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve document {DocumentId} for deletion.", id);
            return Results.Problem("Failed to retrieve the document. Please try again.", statusCode: 503);
        }

        if (doc == null) return Results.NotFound(new { error = $"Document {id} not found." });

        // Refuse to delete a document while it is actively being processed.
        // The background job still holds a reference and will attempt status updates
        // after deletion, which would silently fail and leave the job in a bad state.
        if (doc.Status == DocumentStatus.Processing)
        {
            return Results.Problem(
                "Document is currently being processed and cannot be deleted. Wait for ingestion to complete (status: 'ready' or 'failed') and then retry.",
                statusCode: 409);
        }

        try
        {
            await repo.DeleteDocumentAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete document {DocumentId}.", id);
            return Results.Problem("Failed to delete the document. Please try again.", statusCode: 503);
        }

        return Results.NoContent();
    }
}

public class QueryRequestValidator : AbstractValidator<QueryRequest>
{
    public QueryRequestValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Question is required.")
            // Catches questions that are whitespace-only (e.g. "   ") which pass NotEmpty
            .Must(q => q != null && q.Trim().Length >= 3)
                .WithMessage("Question must be at least 3 non-whitespace characters.")
            .MaximumLength(1000).WithMessage("Question must not exceed 1000 characters.");
    }
}
