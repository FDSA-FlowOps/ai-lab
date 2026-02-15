using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exp05GroundingStrict.Ollama;

public sealed class OllamaChatClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OllamaChatClient(string baseUrl, int timeoutSeconds)
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

    public async Task<string> ChatAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        double temperature,
        double topP,
        int numCtx,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "/api/chat",
                new ChatRequest(
                    model,
                    false,
                    [
                        new ChatMessage("system", systemPrompt),
                        new ChatMessage("user", userPrompt)
                    ],
                    new ChatOptions(temperature, topP, numCtx)),
                cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timeout al llamar Ollama /api/chat en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Ollama en '{_httpClient.BaseAddress}'. Asegura que este levantado.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body404 = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Ollama respondio 404 en '/api/chat'. Detalle: {Trim(body404)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var extra = body.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? $" Posible modelo de chat no encontrado. Ejecuta: ollama pull {model}"
                : string.Empty;
            throw new InvalidOperationException(
                $"Error HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {Trim(body)}{extra}");
        }

        ChatResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<ChatResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Ollama /api/chat.", ex);
        }

        var content = payload?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama /api/chat respondio sin contenido.");
        }

        return content.Trim();
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

    private sealed record ChatRequest(
        string Model,
        bool Stream,
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions Options);

    private sealed class ChatOptions
    {
        public ChatOptions(double temperature, double topP, int numCtx)
        {
            Temperature = temperature;
            TopP = topP;
            NumCtx = numCtx;
        }

        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("top_p")]
        public double TopP { get; init; }

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; init; }
    }
    private sealed record ChatMessage(string Role, string Content);

    private sealed class ChatResponse
    {
        public ChatMessageResponse? Message { get; init; }
    }

    private sealed class ChatMessageResponse
    {
        public string? Content { get; init; }
    }
}
