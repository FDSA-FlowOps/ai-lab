using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exp02VectorDbQdrant.Qdrant;

public sealed class QdrantClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public QdrantClient(string baseUrl, int timeoutSeconds)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"QDRANT_BASE_URL invalido: '{baseUrl}'.", nameof(baseUrl));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    public async Task DeleteCollectionIfExistsAsync(string collection, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.DeleteAsync($"/collections/{collection}", cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. " +
                "Levanta docker-compose para continuar.", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error al borrar coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TrimBody(body));
        }
    }

    public async Task CreateCollectionAsync(string collection, int vectorSize, CancellationToken cancellationToken)
    {
        var req = new CreateCollectionRequest(
            new VectorConfig(vectorSize, "Cosine"));
        var response = await SendPutAsJsonAsync($"/collections/{collection}", req, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error al crear coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TrimBody(body));
        }
    }

    public async Task EnsureCollectionAsync(string collection, int expectedVectorSize, CancellationToken cancellationToken)
    {
        var response = await SendGetAsync($"/collections/{collection}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await CreateCollectionAsync(collection, expectedVectorSize, cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error al consultar coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TrimBody(body));
        }

        CollectionInfoResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CollectionInfoResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Qdrant.", ex);
        }

        var currentSize = payload?.Result?.Config?.Params?.Vectors?.Size;
        if (currentSize is null || currentSize.Value <= 0)
        {
            throw new InvalidOperationException("No se pudo determinar vector size de la coleccion en Qdrant.");
        }

        if (currentSize.Value != expectedVectorSize)
        {
            throw new InvalidOperationException(
                $"Vector size incompatible. Coleccion='{currentSize.Value}', embedding='{expectedVectorSize}'. " +
                "Ejecuta 'dotnet run -- reset' para recrear la coleccion.");
        }
    }

    public async Task UpsertPointsAsync(
        string collection,
        IReadOnlyList<QdrantPoint> points,
        CancellationToken cancellationToken)
    {
        if (points.Count == 0)
        {
            return;
        }

        var request = new UpsertPointsRequest(points);
        var response = await SendPutAsJsonAsync(
            $"/collections/{collection}/points?wait=true",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error upsert en coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TrimBody(body));
        }
    }

    public async Task<List<SearchResult>> SearchAsync(
        string collection,
        float[] vector,
        int topK,
        CancellationToken cancellationToken)
    {
        var req = new SearchPointsRequest
        {
            Vector = vector,
            Limit = topK,
            WithPayload = true
        };
        var response = await SendPostAsJsonAsync(
            $"/collections/{collection}/points/search",
            req,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error en query a coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TrimBody(body));
        }

        SearchResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<SearchResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Qdrant.", ex);
        }

        var output = new List<SearchResult>();
        if (payload?.Result is null)
        {
            return output;
        }

        foreach (var item in payload.Result)
        {
            var p = item.Payload ?? new Dictionary<string, JsonElement>();
            output.Add(new SearchResult
            {
                Score = item.Score,
                DocId = GetStringPayload(p, "doc_id"),
                Title = GetStringPayload(p, "title"),
                Snippet = GetStringPayload(p, "snippet")
            });
        }

        return output;
    }

    private async Task<HttpResponseMessage> SendGetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetAsync(path, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. " +
                "Levanta docker-compose para continuar.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendPutAsJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.PutAsJsonAsync(path, value, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. " +
                "Levanta docker-compose para continuar.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendPostAsJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.PostAsJsonAsync(path, value, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. " +
                "Levanta docker-compose para continuar.", ex);
        }
    }

    private static string GetStringPayload(Dictionary<string, JsonElement> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.ToString()
        };
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(sin detalle)";
        }

        return body.Length <= 300 ? body : body[..300] + "...";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record VectorConfig(int Size, string Distance);

    private sealed record CreateCollectionRequest(VectorConfig Vectors);

    private sealed record UpsertPointsRequest(IReadOnlyList<QdrantPoint> Points);

    private sealed class SearchPointsRequest
    {
        public required float[] Vector { get; init; }
        public required int Limit { get; init; }

        [JsonPropertyName("with_payload")]
        public required bool WithPayload { get; init; }
    }

    private sealed class CollectionInfoResponse
    {
        public CollectionInfoResult? Result { get; init; }
    }

    private sealed class CollectionInfoResult
    {
        public CollectionConfig? Config { get; init; }
    }

    private sealed class CollectionConfig
    {
        public CollectionParams? Params { get; init; }
    }

    private sealed class CollectionParams
    {
        public VectorConfigModel? Vectors { get; init; }
    }

    private sealed class VectorConfigModel
    {
        public int? Size { get; init; }
    }

    private sealed class SearchResponse
    {
        public SearchItem[]? Result { get; init; }
    }

    private sealed class SearchItem
    {
        public double Score { get; init; }
        public Dictionary<string, JsonElement>? Payload { get; init; }
    }
}

public sealed class QdrantPoint
{
    public required int Id { get; init; }
    public required float[] Vector { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
}

public sealed class SearchResult
{
    public double Score { get; init; }
    public string DocId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
}
