using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Exp01EmbeddingsOllama.Ollama;

public sealed class OllamaEmbeddingClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OllamaEmbeddingClient(string baseUrl, int timeoutSeconds)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"OLLAMA_BASE_URL invalido: '{baseUrl}'.", nameof(baseUrl));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    public async Task<float[]> GetEmbeddingAsync(string model, string text, CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest(model, text);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/api/embeddings", request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timeout al llamar a Ollama. Revisa HTTP_TIMEOUT_SECONDS o disponibilidad en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Ollama en '{_httpClient.BaseAddress}'. Asegura que este corriendo.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body404 = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                "Ollama devolvio 404. Revisa endpoint '/api/embeddings' o modelo instalado. " +
                $"Detalle: {TrimBody(body404)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var extra = body.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? " Posible modelo no encontrado. Ejecuta: ollama pull bge-m3"
                : string.Empty;
            throw new InvalidOperationException(
                $"Error HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {TrimBody(body)}{extra}");
        }

        EmbeddingResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Ollama.", ex);
        }

        if (payload?.Embedding is null || payload.Embedding.Length == 0)
        {
            throw new InvalidOperationException("Ollama respondio sin vector 'embedding' valido.");
        }

        return payload.Embedding;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(sin detalle)";
        }

        return body.Length <= 300 ? body : body[..300] + "...";
    }

    private sealed record EmbeddingRequest(string Model, string Prompt);

    private sealed class EmbeddingResponse
    {
        public float[]? Embedding { get; init; }
    }
}
