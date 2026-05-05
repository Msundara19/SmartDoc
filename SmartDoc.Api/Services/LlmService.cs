using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SmartDoc.Api.Models;

namespace SmartDoc.Api.Services;

public interface ILlmService
{
    Task<string> GenerateAnswerAsync(string question, IEnumerable<ScoredChunk> context, CancellationToken ct = default);
    Task<List<Flashcard>> GenerateFlashcardsAsync(IEnumerable<Chunk> chunks, CancellationToken ct = default);
}

public class GroqLlmService : ILlmService
{
    private const string Model = "llama-3.3-70b-versatile";
    private const int MaxTokens = 500;
    private const int MaxFlashcardTokens = 700;
    private const int ContextChunkCharLimit = 600;

    private readonly HttpClient _http;

    public GroqLlmService(HttpClient http, IConfiguration config)
    {
        _http = http;
        var apiKey = config["Groq:ApiKey"]
            ?? throw new InvalidOperationException("Groq:ApiKey is not configured.");
        _http.BaseAddress = new Uri("https://api.groq.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> GenerateAnswerAsync(
        string question, IEnumerable<ScoredChunk> context, CancellationToken ct = default)
    {
        var contextText = BuildContextText(context);

        var messages = new[]
        {
            new
            {
                role = "system",
                content = "You are a document Q&A assistant. Answer ONLY from the provided context chunks. " +
                          "Cite page numbers where relevant (e.g. \"According to page 4, ...\"). " +
                          "If the context lacks enough information, say so. Be concise."
            },
            new
            {
                role = "user",
                content = $"""
                    Context from the document:
                    {contextText}

                    Question: {question}

                    Answer based solely on the context above:
                    """
            }
        };

        var payload = new
        {
            model = Model,
            messages,
            max_tokens = MaxTokens,
            temperature = 0.1
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("openai/v1/chat/completions", content, ct);

        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException(
                "The AI service is temporarily rate-limited. Please wait a few seconds and try again.");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<GroqResponse>(body)
            ?? throw new InvalidOperationException("Empty response from Groq API.");

        if (result.Choices is not { Count: > 0 })
            throw new InvalidOperationException("Groq API returned a response with no choices.");

        var answerText = result.Choices[0].Message?.Content;
        if (string.IsNullOrWhiteSpace(answerText))
            throw new InvalidOperationException("Groq API returned an empty answer.");

        return answerText.Trim();
    }

    public async Task<List<Flashcard>> GenerateFlashcardsAsync(
        IEnumerable<Chunk> chunks, CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();
        var contextText = BuildFlashcardContext(chunkList);

        var messages = new[]
        {
            new
            {
                role = "system",
                content = "You are a study flashcard generator. Output ONLY a valid JSON array. " +
                          "No markdown, no code blocks, no preamble — raw JSON only."
            },
            new
            {
                role = "user",
                content = $$"""
                    Generate 8-12 flashcards from these document chunks.
                    Return ONLY a JSON array in exactly this format (no extra text):
                    [{"front":"question or term","back":"answer or definition","page":3,"section":"3.1 Intro"}]

                    Rules:
                    1. front: concise question or key term (under 12 words)
                    2. back: clear, complete answer (under 60 words)
                    3. page: integer page number from the chunk header — omit field if not present
                    4. section: section name from the chunk header — omit field if not present
                    5. Cover diverse topics spread across all chunks

                    Document chunks:
                    {{contextText}}
                    """
            }
        };

        var payload = new { model = Model, messages, max_tokens = MaxFlashcardTokens, temperature = 0.2 };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("openai/v1/chat/completions", content, ct);

        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException(
                "The AI service is temporarily rate-limited. Please wait a few seconds and try again.");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<GroqResponse>(body)
            ?? throw new InvalidOperationException("Empty response from Groq API.");

        if (result.Choices is not { Count: > 0 })
            throw new InvalidOperationException("Groq API returned a response with no choices.");

        var rawJson = result.Choices[0].Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidOperationException("Groq API returned empty flashcard content.");

        return ParseFlashcards(rawJson);
    }

    private static List<Flashcard> ParseFlashcards(string rawJson)
    {
        var trimmed = rawJson.Trim();

        // Strip markdown code fences if the model wrapped the JSON
        if (trimmed.StartsWith("```"))
        {
            var s = trimmed.IndexOf('[');
            var e = trimmed.LastIndexOf(']');
            if (s >= 0 && e > s) trimmed = trimmed[s..(e + 1)];
        }
        else
        {
            var s = trimmed.IndexOf('[');
            var e = trimmed.LastIndexOf(']');
            if (s >= 0 && e > s) trimmed = trimmed[s..(e + 1)];
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<FlashcardDto>>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return dtos?
                .Where(d => !string.IsNullOrWhiteSpace(d.Front) && !string.IsNullOrWhiteSpace(d.Back))
                .Select(d => new Flashcard { Front = d.Front!, Back = d.Back!, Page = d.Page, Section = d.Section })
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string BuildFlashcardContext(List<Chunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var page = chunk.PageNumber.HasValue ? $"Page {chunk.PageNumber}" : "Unknown page";
            var section = chunk.SectionName != null ? $" | Section: {chunk.SectionName}" : "";
            var text = chunk.ChunkText.Length > ContextChunkCharLimit
                ? chunk.ChunkText[..ContextChunkCharLimit] + "…"
                : chunk.ChunkText;
            sb.AppendLine($"[{page}{section}]");
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private record FlashcardDto(
        [property: JsonPropertyName("front")]   string? Front,
        [property: JsonPropertyName("back")]    string? Back,
        [property: JsonPropertyName("page")]    int? Page,
        [property: JsonPropertyName("section")] string? Section);

    private static string BuildContextText(IEnumerable<ScoredChunk> chunks)
    {
        var sb = new StringBuilder();
        int i = 1;
        foreach (var sc in chunks)
        {
            var page = sc.Chunk.PageNumber.HasValue ? $"Page {sc.Chunk.PageNumber}" : "Unknown page";
            var section = sc.Chunk.SectionName != null ? $" | Section: {sc.Chunk.SectionName}" : "";
            var text = sc.Chunk.ChunkText.Length > ContextChunkCharLimit
                ? sc.Chunk.ChunkText[..ContextChunkCharLimit] + "…"
                : sc.Chunk.ChunkText;
            sb.AppendLine($"[Chunk {i}{section} | {page}]");
            sb.AppendLine(text);
            sb.AppendLine();
            i++;
        }
        return sb.ToString();
    }

    private record GroqResponse(
        [property: JsonPropertyName("choices")] List<GroqChoice> Choices);

    private record GroqChoice(
        [property: JsonPropertyName("message")] GroqMessage Message);

    private record GroqMessage(
        [property: JsonPropertyName("content")] string Content);
}
