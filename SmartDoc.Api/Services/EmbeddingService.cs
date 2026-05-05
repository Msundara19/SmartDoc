using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SmartDoc.Api.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}

/// <summary>
/// Local embeddings via Ollama (nomic-embed-text, 768 dims). Free, no API key required.
/// Requires: ollama pull nomic-embed-text && ollama serve
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private const string Model = "nomic-embed-text";
    private readonly HttpClient _http;

    public OllamaEmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        var baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var list = texts.ToList();
        var payload = new { model = Model, input = list };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("/api/embed", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(body)
            ?? throw new InvalidOperationException("Empty response from Ollama embed API.");

        return result.Embeddings;
    }

    private record OllamaEmbedResponse(
        [property: JsonPropertyName("embeddings")] List<float[]> Embeddings);
}

public class OpenAiEmbeddingService : IEmbeddingService
{
    private const string Model = "text-embedding-3-small";
    private const int BatchSize = 20;   // smaller batches to stay under rate limits
    private const int MaxRetries = 5;

    private readonly HttpClient _http;

    public OpenAiEmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        _http.BaseAddress = new Uri("https://api.openai.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var allTexts = texts.ToList();
        var allEmbeddings = new List<float[]>(allTexts.Count);

        for (int i = 0; i < allTexts.Count; i += BatchSize)
        {
            var batch = allTexts.Skip(i).Take(BatchSize).ToList();
            var embeddings = await EmbedBatchWithRetryAsync(batch, ct);
            allEmbeddings.AddRange(embeddings);

            // Small pause between batches to respect rate limits
            if (i + BatchSize < allTexts.Count)
                await Task.Delay(200, ct);
        }

        return allEmbeddings;
    }

    private async Task<List<float[]>> EmbedBatchWithRetryAsync(List<string> texts, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var payload = new { model = Model, input = texts };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("v1/embeddings", content, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxRetries)
                    throw new InvalidOperationException(
                        "OpenAI rate limit exceeded after maximum retries. Try again in a minute.");

                // Respect Retry-After header if present, otherwise exponential backoff
                int waitMs = 1000 * (int)Math.Pow(2, attempt);
                if (response.Headers.TryGetValues("Retry-After", out var vals)
                    && int.TryParse(vals.FirstOrDefault(), out int retryAfter))
                {
                    waitMs = (retryAfter + 1) * 1000;
                }

                await Task.Delay(waitMs, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(body)
                ?? throw new InvalidOperationException("Empty response from OpenAI embeddings API.");

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToList();
        }

        throw new InvalidOperationException("Failed to get embeddings after all retries.");
    }

    private record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
