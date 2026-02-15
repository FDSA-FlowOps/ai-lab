using System.Globalization;
using System.Text.Json;

namespace Exp04RagBasicQdrant.Models;

public sealed class AppConfig
{
    public required string OllamaBaseUrl { get; init; }
    public required string OllamaEmbedModel { get; init; }
    public required string OllamaChatModel { get; init; }
    public required string QdrantBaseUrl { get; init; }
    public required string QdrantCollection { get; init; }
    public required int TopK { get; init; }
    public required double MinRetrievalScore { get; init; }
    public required int ChunkSizeChars { get; init; }
    public required int ChunkOverlapChars { get; init; }
    public required int MinChunkChars { get; init; }
    public required int HttpTimeoutSeconds { get; init; }
    public required bool ShowDebug { get; init; }

    public static AppConfig Load(string root)
    {
        var appSettingsPath = Path.Combine(root, "appsettings.json");
        var file = File.Exists(appSettingsPath)
            ? JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText(appSettingsPath))
            : null;

        var topK = ParsePositiveInt(GetValue("TOP_K", file?.TopK?.ToString(), "6"), "TOP_K");
        var minScore = ParseDoubleFlexible(GetValue("MIN_RETRIEVAL_SCORE", file?.MinRetrievalScore?.ToString(CultureInfo.InvariantCulture), "0.60"), "MIN_RETRIEVAL_SCORE");
        var chunkSize = ParsePositiveInt(GetValue("CHUNK_SIZE_CHARS", file?.ChunkSizeChars?.ToString(), "900"), "CHUNK_SIZE_CHARS");
        var chunkOverlap = ParseNonNegativeInt(GetValue("CHUNK_OVERLAP_CHARS", file?.ChunkOverlapChars?.ToString(), "140"), "CHUNK_OVERLAP_CHARS");
        var minChunk = ParsePositiveInt(GetValue("MIN_CHUNK_CHARS", file?.MinChunkChars?.ToString(), "220"), "MIN_CHUNK_CHARS");
        var timeout = ParsePositiveInt(GetValue("HTTP_TIMEOUT_SECONDS", file?.HttpTimeoutSeconds?.ToString(), "60"), "HTTP_TIMEOUT_SECONDS");
        var showDebug = ParseBool(GetValue("SHOW_DEBUG", file?.ShowDebug?.ToString(), "true"), "SHOW_DEBUG");

        if (chunkOverlap >= chunkSize)
        {
            throw new InvalidOperationException("CHUNK_OVERLAP_CHARS debe ser menor que CHUNK_SIZE_CHARS.");
        }

        return new AppConfig
        {
            OllamaBaseUrl = GetValue("OLLAMA_BASE_URL", file?.OllamaBaseUrl, "http://localhost:11434"),
            OllamaEmbedModel = GetValue("OLLAMA_EMBED_MODEL", file?.OllamaEmbedModel, "bge-m3"),
            OllamaChatModel = GetValue("OLLAMA_CHAT_MODEL", file?.OllamaChatModel, "llama3.1:8b"),
            QdrantBaseUrl = GetValue("QDRANT_BASE_URL", file?.QdrantBaseUrl, "http://localhost:6333"),
            QdrantCollection = GetValue("QDRANT_COLLECTION", file?.QdrantCollection, "exp04_rag"),
            TopK = topK,
            MinRetrievalScore = minScore,
            ChunkSizeChars = chunkSize,
            ChunkOverlapChars = chunkOverlap,
            MinChunkChars = minChunk,
            HttpTimeoutSeconds = timeout,
            ShowDebug = showDebug
        };
    }

    public RuntimeSettings ToRuntimeSettings()
    {
        return new RuntimeSettings
        {
            TopK = TopK,
            MinRetrievalScore = MinRetrievalScore,
            ShowDebug = ShowDebug,
            ChunkSizeChars = ChunkSizeChars,
            ChunkOverlapChars = ChunkOverlapChars,
            MinChunkChars = MinChunkChars
        };
    }

    private static string GetValue(string envKey, string? appValue, string fallback)
    {
        var env = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return !string.IsNullOrWhiteSpace(appValue) ? appValue : fallback;
    }

    private static int ParsePositiveInt(string raw, string key)
    {
        if (!int.TryParse(raw, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"{key} invalido: '{raw}'. Debe ser entero > 0.");
        }

        return value;
    }

    private static int ParseNonNegativeInt(string raw, string key)
    {
        if (!int.TryParse(raw, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{key} invalido: '{raw}'. Debe ser entero >= 0.");
        }

        return value;
    }

    private static double ParseDoubleFlexible(string raw, string key)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            return v;
        }

        var normalized = raw.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            return v;
        }

        throw new InvalidOperationException($"{key} invalido: '{raw}'.");
    }

    private static bool ParseBool(string raw, string key)
    {
        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"{key} invalido: '{raw}'. Usa true/false.");
    }

    private sealed class AppSettingsFile
    {
        public string? OllamaBaseUrl { get; init; }
        public string? OllamaEmbedModel { get; init; }
        public string? OllamaChatModel { get; init; }
        public string? QdrantBaseUrl { get; init; }
        public string? QdrantCollection { get; init; }
        public int? TopK { get; init; }
        public double? MinRetrievalScore { get; init; }
        public int? ChunkSizeChars { get; init; }
        public int? ChunkOverlapChars { get; init; }
        public int? MinChunkChars { get; init; }
        public int? HttpTimeoutSeconds { get; init; }
        public bool? ShowDebug { get; init; }
    }
}
