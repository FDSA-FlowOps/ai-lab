using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Exp04RagBasicQdrant.Models;

namespace Exp04RagBasicQdrant.Qdrant;

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
        var response = await SendDeleteAsync($"/collections/{collection}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error al borrar coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
        }
    }

    public async Task CreateCollectionAsync(string collection, int vectorSize, CancellationToken cancellationToken)
    {
        var request = new CreateCollectionRequest(new VectorParams(vectorSize, "Cosine"));
        var response = await SendPutAsJsonAsync($"/collections/{collection}", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error al crear coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
        }
    }

    public async Task EnsureCollectionAsync(string collection, int vectorSize, CancellationToken cancellationToken)
    {
        var response = await SendGetAsync($"/collections/{collection}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await CreateCollectionAsync(collection, vectorSize, cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error consultando coleccion '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
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

        var current = payload?.Result?.Config?.Params?.Vectors?.Size;
        if (current is null || current.Value <= 0)
        {
            throw new InvalidOperationException("No se pudo detectar vector size de la coleccion.");
        }

        if (current.Value != vectorSize)
        {
            throw new InvalidOperationException(
                $"Vector size incompatible. Coleccion={current.Value}, embedding={vectorSize}. Ejecuta Reset collection.");
        }
    }

    public async Task UpsertPointsAsync(string collection, IReadOnlyList<QdrantPoint> points, CancellationToken cancellationToken)
    {
        if (points.Count == 0)
        {
            return;
        }

        var response = await SendPutAsJsonAsync(
            $"/collections/{collection}/points?wait=true",
            new UpsertRequest(points),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error en upsert '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
        }
    }

    public async Task<long> CountPointsAsync(string collection, CancellationToken cancellationToken)
    {
        var response = await SendPostAsJsonAsync(
            $"/collections/{collection}/points/count",
            new CountRequest { Exact = true },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return 0;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error consultando count en '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
        }

        CountResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CountResponse>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Respuesta JSON invalida desde Qdrant.", ex);
        }

        return payload?.Result?.Count ?? 0;
    }

    public async Task<List<RetrievedChunk>> SearchChunksAsync(
        string collection,
        float[] vector,
        int topK,
        CancellationToken cancellationToken)
    {
        var req = new SearchRequest
        {
            Vector = vector,
            Limit = topK,
            WithPayload = true
        };

        var response = await SendPostAsJsonAsync($"/collections/{collection}/points/search", req, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error en search '{collection}': {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(body)}");
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

        var output = new List<RetrievedChunk>();
        if (payload?.Result is null)
        {
            return output;
        }

        foreach (var item in payload.Result)
        {
            var p = item.Payload ?? new Dictionary<string, JsonElement>();
            output.Add(new RetrievedChunk
            {
                Score = item.Score,
                DocId = GetString(p, "doc_id"),
                DocTitle = GetString(p, "doc_title"),
                SectionTitle = GetString(p, "section_title"),
                ChunkId = GetString(p, "chunk_id"),
                ChunkIndex = GetInt(p, "chunk_index"),
                ChunkText = GetString(p, "chunk_text")
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
            throw new InvalidOperationException($"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. Levanta docker-compose para continuar.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendDeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.DeleteAsync(path, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. Levanta docker-compose para continuar.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendPutAsJsonAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.PutAsJsonAsync(path, payload, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. Levanta docker-compose para continuar.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendPostAsJsonAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.PostAsJsonAsync(path, payload, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timeout al llamar Qdrant en '{_httpClient.BaseAddress}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"No se pudo conectar a Qdrant en '{_httpClient.BaseAddress}'. Levanta docker-compose para continuar.", ex);
        }
    }

    private static string GetString(Dictionary<string, JsonElement> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString();
    }

    private static int GetInt(Dictionary<string, JsonElement> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v))
        {
            return -1;
        }

        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : int.TryParse(v.ToString(), out var parsed) ? parsed : -1;
    }

    private static string Trim(string body)
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

    private sealed record CreateCollectionRequest(VectorParams Vectors);
    private sealed record VectorParams(int Size, string Distance);
    private sealed record UpsertRequest(IReadOnlyList<QdrantPoint> Points);

    private sealed class CountRequest
    {
        [JsonPropertyName("exact")]
        public required bool Exact { get; init; }
    }

    private sealed class SearchRequest
    {
        public required float[] Vector { get; init; }
        public required int Limit { get; init; }

        [JsonPropertyName("with_payload")]
        public required bool WithPayload { get; init; }
    }

    private sealed class CountResponse
    {
        public CountResult? Result { get; init; }
    }

    private sealed class CountResult
    {
        public long Count { get; init; }
    }

    private sealed class CollectionInfoResponse
    {
        public CollectionResult? Result { get; init; }
    }

    private sealed class CollectionResult
    {
        public CollectionConfig? Config { get; init; }
    }

    private sealed class CollectionConfig
    {
        public CollectionParams? Params { get; init; }
    }

    private sealed class CollectionParams
    {
        public VectorInfo? Vectors { get; init; }
    }

    private sealed class VectorInfo
    {
        public int? Size { get; init; }
    }

    private sealed class SearchResponse
    {
        public SearchPoint[]? Result { get; init; }
    }

    private sealed class SearchPoint
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
