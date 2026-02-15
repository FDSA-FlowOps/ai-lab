using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Exp04RagBasicQdrant.Ollama;

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

    public async Task<float[]> GetEmbeddingAsync(string model, string input, CancellationToken cancellationToken)
    {
        var all = await GetEmbeddingsAsync(model, [input], cancellationToken);
        return all[0];
    }

    public async Task<float[][]> GetEmbeddingsAsync(string model, IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            throw new ArgumentException("Debes enviar al menos un texto para embeddings.", nameof(inputs));
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "/api/embed",
                new EmbedRequest(model, inputs.ToArray()),
                cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timeout al llamar Ollama en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Ollama en '{_httpClient.BaseAddress}'. Asegura que este levantado.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Ollama respondio 404 en '/api/embed'. Detalle: {Trim(body)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var extra = body.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? $" Posible modelo no encontrado. Ejecuta: ollama pull {model}"
                : string.Empty;
            throw new InvalidOperationException(
                $"Error HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {Trim(body)}{extra}");
        }

        EmbedResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<EmbedResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Ollama.", ex);
        }

        if (payload?.Embeddings is { Length: > 0 })
        {
            return payload.Embeddings;
        }

        if (payload?.Embedding is { Length: > 0 })
        {
            return [payload.Embedding];
        }

        throw new InvalidOperationException("Ollama respondio sin 'embeddings' validos.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string Trim(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(sin detalle)";
        }

        return body.Length <= 300 ? body : body[..300] + "...";
    }

    private sealed record EmbedRequest(string Model, string[] Input);

    private sealed class EmbedResponse
    {
        public float[][]? Embeddings { get; init; }
        public float[]? Embedding { get; init; }
    }
}
