using System.Text.Json;
using Exp03ChunkingQdrant.Chunking;
using Exp03ChunkingQdrant.Ollama;
using Exp03ChunkingQdrant.Qdrant;

const int EmbedBatchSize = 24;

try
{
    var root = ResolveExperimentRoot();
    var config = AppConfig.Load(root);
    var runtime = RuntimeSettings.From(config);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    using var ollama = new OllamaEmbeddingClient(config.OllamaBaseUrl, config.HttpTimeoutSeconds);
    using var qdrant = new QdrantClient(config.QdrantBaseUrl, config.HttpTimeoutSeconds);

    await RunInteractiveMenuAsync(root, config, runtime, ollama, qdrant, cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("[ERROR] Operacion cancelada.");
    return 1;
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    return 1;
}

static async Task RunInteractiveMenuAsync(
    string root,
    AppConfig config,
    RuntimeSettings runtime,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        PrintMenu(config, runtime);
        Console.Write("Selecciona una opcion (1-6): ");
        var option = Console.ReadLine()?.Trim();

        try
        {
            switch (option)
            {
                case "1":
                    await ResetCollectionAsync(root, config, runtime, ollama, qdrant, cancellationToken);
                    break;
                case "2":
                    await IngestAsync(root, config, runtime, ollama, qdrant, cancellationToken);
                    break;
                case "3":
                    await QueryAsync(config, runtime, ollama, qdrant, cancellationToken);
                    break;
                case "4":
                    ShowChunkingPreview(root, runtime);
                    break;
                case "5":
                    ChangeSettings(runtime);
                    break;
                case "6":
                    return;
                default:
                    Console.WriteLine("[WARN] Opcion invalida.");
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
        }

        Console.WriteLine();
        Console.Write("Presiona ENTER para continuar...");
        Console.ReadLine();
        TryClearConsole();
    }
}

static void TryClearConsole()
{
    try
    {
        Console.Clear();
    }
    catch (IOException)
    {
    }
    catch (PlatformNotSupportedException)
    {
    }
}

static async Task ResetCollectionAsync(
    string root,
    AppConfig config,
    RuntimeSettings runtime,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    var docs = LoadDocuments(root);
    var chunks = BuildAllChunks(docs, runtime.Chunking);
    if (chunks.Count == 0)
    {
        throw new InvalidOperationException("No se pudieron generar chunks. Ajusta la configuracion.");
    }

    Console.WriteLine("[INFO] Calculando embedding inicial para detectar dimension...");
    var probe = await ollama.GetEmbeddingAsync(config.OllamaModel, chunks[0].ChunkText, cancellationToken);
    await qdrant.DeleteCollectionIfExistsAsync(config.QdrantCollection, cancellationToken);
    await qdrant.CreateCollectionAsync(config.QdrantCollection, probe.Length, cancellationToken);
    Console.WriteLine($"[OK] Coleccion '{config.QdrantCollection}' recreada (vector size={probe.Length}).");
}

static async Task IngestAsync(
    string root,
    AppConfig config,
    RuntimeSettings runtime,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    var docs = LoadDocuments(root);
    var chunks = BuildAllChunks(docs, runtime.Chunking);
    if (chunks.Count == 0)
    {
        throw new InvalidOperationException("No se generaron chunks para ingest.");
    }

    Console.WriteLine($"[INFO] Iniciando ingest: {docs.Count} docs, {chunks.Count} chunks.");

    var firstBatchCount = Math.Min(EmbedBatchSize, chunks.Count);
    var firstBatchTexts = chunks.Take(firstBatchCount).Select(c => c.ChunkText).ToArray();
    var firstEmbeddings = await ollama.GetEmbeddingsAsync(config.OllamaModel, firstBatchTexts, cancellationToken);
    if (firstEmbeddings.Length != firstBatchCount)
    {
        throw new InvalidOperationException("Cantidad inesperada de embeddings en primer batch.");
    }

    await qdrant.EnsureCollectionAsync(config.QdrantCollection, firstEmbeddings[0].Length, cancellationToken);

    var points = new List<QdrantPoint>(chunks.Count);
    for (var i = 0; i < firstBatchCount; i++)
    {
        points.Add(ToPoint(i, chunks[i], firstEmbeddings[i]));
    }

    for (var start = firstBatchCount; start < chunks.Count; start += EmbedBatchSize)
    {
        var count = Math.Min(EmbedBatchSize, chunks.Count - start);
        Console.WriteLine($"[INFO] Embedding chunks {start + 1}-{start + count}/{chunks.Count}...");
        var texts = chunks.Skip(start).Take(count).Select(c => c.ChunkText).ToArray();
        var vectors = await ollama.GetEmbeddingsAsync(config.OllamaModel, texts, cancellationToken);
        if (vectors.Length != count)
        {
            throw new InvalidOperationException("Cantidad inesperada de embeddings en batch.");
        }

        for (var i = 0; i < count; i++)
        {
            var point = ToPoint(start + i, chunks[start + i], vectors[i]);
            points.Add(point);
        }
    }

    await qdrant.UpsertPointsAsync(config.QdrantCollection, points, cancellationToken);

    var lengths = chunks.Select(c => c.ChunkText.Length).ToList();
    var min = lengths.Min();
    var max = lengths.Max();
    var avg = lengths.Average();
    Console.WriteLine(
        $"[OK] Ingest completado. docs={docs.Count}, chunks={chunks.Count}, min={min}, avg={avg:0.0}, max={max} chars.");
}

static async Task QueryAsync(
    AppConfig config,
    RuntimeSettings runtime,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    var count = await qdrant.CountPointsAsync(config.QdrantCollection, cancellationToken);
    if (count == 0)
    {
        Console.WriteLine("[WARN] La coleccion esta vacia. Ejecuta primero la opcion 2) Ingest.");
        return;
    }

    Console.Write("Texto de query: ");
    var query = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.WriteLine("[WARN] Query vacia.");
        return;
    }

    Console.WriteLine("[INFO] Generando embedding de query...");
    var vector = await ollama.GetEmbeddingAsync(config.OllamaModel, query, cancellationToken);
    var results = await qdrant.SearchAsync(config.QdrantCollection, vector, runtime.TopK, cancellationToken);

    Console.WriteLine();
    Console.WriteLine($"Top {runtime.TopK} resultados:");
    if (results.Count == 0)
    {
        Console.WriteLine("Sin resultados.");
        return;
    }

    for (var i = 0; i < results.Count; i++)
    {
        var r = results[i];
        Console.WriteLine($"{i + 1}. score={r.Score:0.0000} | doc={r.DocId} | section={r.SectionTitle} | chunk={r.ChunkIndex}");
        Console.WriteLine($"   preview: {Trim(r.ChunkText, 140)}");
    }
}

static void ShowChunkingPreview(string root, RuntimeSettings runtime)
{
    var docs = LoadDocuments(root);
    if (docs.Count == 0)
    {
        Console.WriteLine("[WARN] No hay documentos en ./data.");
        return;
    }

    Console.WriteLine("Documentos disponibles:");
    for (var i = 0; i < docs.Count; i++)
    {
        Console.WriteLine($"{i + 1}) {docs[i].DocId} | {docs[i].Title}");
    }

    Console.Write("Elige documento (numero): ");
    if (!int.TryParse(Console.ReadLine(), out var idx) || idx < 1 || idx > docs.Count)
    {
        Console.WriteLine("[WARN] Seleccion invalida.");
        return;
    }

    Console.Write("Cuantos chunks mostrar (default 5): ");
    var rawN = Console.ReadLine();
    var n = int.TryParse(rawN, out var parsedN) && parsedN > 0 ? parsedN : 5;

    var selected = docs[idx - 1];
    var chunks = Chunker.BuildChunks(selected, runtime.Chunking);
    Console.WriteLine($"[INFO] {selected.DocId}: total chunks={chunks.Count}");
    foreach (var c in chunks.Take(n))
    {
        Console.WriteLine($"- [{c.ChunkIndex}] chars={c.ChunkText.Length} start={c.StartChar} end={c.EndChar} section={c.SectionTitle}");
        Console.WriteLine($"  {Trim(c.ChunkText, 180)}");
    }
}

static void ChangeSettings(RuntimeSettings runtime)
{
    Console.WriteLine($"Estrategia actual: {runtime.Chunking.Strategy}");
    Console.Write("Nueva estrategia (MarkdownAware / FixedSizeWithOverlap, ENTER para mantener): ");
    var rawStrategy = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(rawStrategy))
    {
        if (!Enum.TryParse<ChunkStrategy>(rawStrategy, ignoreCase: true, out var strategy))
        {
            Console.WriteLine("[WARN] Estrategia invalida, se mantiene valor actual.");
        }
        else
        {
            runtime.Chunking.Strategy = strategy;
        }
    }

    runtime.Chunking.ChunkSizeChars = PromptInt("Chunk size chars", runtime.Chunking.ChunkSizeChars);
    runtime.Chunking.ChunkOverlapChars = PromptInt("Chunk overlap chars", runtime.Chunking.ChunkOverlapChars);
    runtime.TopK = PromptInt("TopK", runtime.TopK);

    ValidateChunking(runtime.Chunking);
    Console.WriteLine("[OK] Settings actualizados para esta sesion.");
}

static int PromptInt(string label, int current)
{
    Console.Write($"{label} (actual={current}, ENTER para mantener): ");
    var raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return current;
    }

    if (!int.TryParse(raw, out var parsed) || parsed <= 0)
    {
        Console.WriteLine("[WARN] Valor invalido, se mantiene actual.");
        return current;
    }

    return parsed;
}

static QdrantPoint ToPoint(int id, DocumentChunk chunk, float[] vector)
{
    return new QdrantPoint
    {
        Id = id + 1,
        Vector = vector,
        Payload = new Dictionary<string, object?>
        {
            ["doc_id"] = chunk.DocId,
            ["doc_title"] = chunk.DocTitle,
            ["chunk_id"] = chunk.ChunkId,
            ["chunk_index"] = chunk.ChunkIndex,
            ["chunk_text"] = chunk.ChunkText,
            ["section_title"] = chunk.SectionTitle,
            ["start_char"] = chunk.StartChar,
            ["end_char"] = chunk.EndChar,
            ["strategy"] = chunk.Strategy.ToString(),
            ["chunk_size"] = chunk.ChunkSize,
            ["overlap"] = chunk.Overlap
        }
    };
}

static List<DocumentData> LoadDocuments(string root)
{
    var dataPath = Path.Combine(root, "data");
    if (!Directory.Exists(dataPath))
    {
        throw new InvalidOperationException($"No existe carpeta data en '{root}'.");
    }

    var files = Directory.GetFiles(dataPath, "*.md", SearchOption.TopDirectoryOnly)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (files.Count == 0)
    {
        throw new InvalidOperationException("No se encontraron archivos .md en ./data.");
    }

    var docs = new List<DocumentData>(files.Count);
    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        var docId = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
        var title = FirstMarkdownTitle(text) ?? Path.GetFileNameWithoutExtension(file);
        docs.Add(new DocumentData(docId, title, text, file));
    }

    return docs;
}

static List<DocumentChunk> BuildAllChunks(IReadOnlyList<DocumentData> docs, ChunkingSettings settings)
{
    ValidateChunking(settings);

    var all = new List<DocumentChunk>();
    foreach (var doc in docs)
    {
        var chunks = Chunker.BuildChunks(doc, settings);
        all.AddRange(chunks);
    }

    return all;
}

static void ValidateChunking(ChunkingSettings settings)
{
    if (settings.ChunkSizeChars <= 0 || settings.ChunkOverlapChars < 0)
    {
        throw new InvalidOperationException("Chunk size/overlap deben ser positivos.");
    }

    if (settings.ChunkOverlapChars >= settings.ChunkSizeChars)
    {
        throw new InvalidOperationException("Chunk overlap debe ser menor que chunk size.");
    }

    if (settings.MinChunkChars <= 0 || settings.MaxChunkChars <= 0)
    {
        throw new InvalidOperationException("Min/Max chunk chars deben ser positivos.");
    }

    if (settings.MinChunkChars > settings.MaxChunkChars)
    {
        throw new InvalidOperationException("Min chunk chars no puede ser mayor que Max chunk chars.");
    }
}

static string? FirstMarkdownTitle(string text)
{
    using var reader = new StringReader(text);
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("# "))
        {
            return trimmed[2..].Trim();
        }
    }

    return null;
}

static string Trim(string text, int max)
{
    var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
    return clean.Length <= max ? clean : clean[..max] + "...";
}

static string ResolveExperimentRoot()
{
    var candidates = new List<string>
    {
        Directory.GetCurrentDirectory(),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
        AppContext.BaseDirectory
    };

    foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (Directory.Exists(Path.Combine(c, "data")))
        {
            return c;
        }
    }

    return Directory.GetCurrentDirectory();
}

static void PrintMenu(AppConfig config, RuntimeSettings runtime)
{
    Console.WriteLine("=== EXP-03 Chunking + Qdrant ===");
    Console.WriteLine($"Collection: {config.QdrantCollection}");
    Console.WriteLine($"Model: {config.OllamaModel}");
    Console.WriteLine($"Strategy: {runtime.Chunking.Strategy} | Size={runtime.Chunking.ChunkSizeChars} | Overlap={runtime.Chunking.ChunkOverlapChars} | TopK={runtime.TopK}");
    Console.WriteLine();
    Console.WriteLine("1) Reset collection");
    Console.WriteLine("2) Ingest");
    Console.WriteLine("3) Query");
    Console.WriteLine("4) Show chunking preview");
    Console.WriteLine("5) Change settings");
    Console.WriteLine("6) Exit");
    Console.WriteLine();
}

sealed class RuntimeSettings
{
    public required int TopK { get; set; }
    public required ChunkingSettings Chunking { get; set; }

    public static RuntimeSettings From(AppConfig config)
    {
        return new RuntimeSettings
        {
            TopK = config.TopK,
            Chunking = new ChunkingSettings
            {
                Strategy = config.ChunkStrategy,
                ChunkSizeChars = config.ChunkSizeChars,
                ChunkOverlapChars = config.ChunkOverlapChars,
                MinChunkChars = config.MinChunkChars,
                MaxChunkChars = config.MaxChunkChars
            }
        };
    }
}

sealed class AppConfig
{
    public required string OllamaBaseUrl { get; init; }
    public required string OllamaModel { get; init; }
    public required string QdrantBaseUrl { get; init; }
    public required string QdrantCollection { get; init; }
    public required int TopK { get; init; }
    public required ChunkStrategy ChunkStrategy { get; init; }
    public required int ChunkSizeChars { get; init; }
    public required int ChunkOverlapChars { get; init; }
    public required int MinChunkChars { get; init; }
    public required int MaxChunkChars { get; init; }
    public required int HttpTimeoutSeconds { get; init; }

    public static AppConfig Load(string root)
    {
        var settingsPath = Path.Combine(root, "appsettings.json");
        var file = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText(settingsPath))
            : null;

        var strategyRaw = GetValue("CHUNK_STRATEGY", file?.ChunkStrategy, "MarkdownAware");
        if (!Enum.TryParse<ChunkStrategy>(strategyRaw, ignoreCase: true, out var strategy))
        {
            throw new InvalidOperationException($"CHUNK_STRATEGY invalido: '{strategyRaw}'.");
        }

        var topK = ParsePositiveInt(GetValue("TOP_K", file?.TopK?.ToString(), "5"), "TOP_K");
        var chunkSize = ParsePositiveInt(GetValue("CHUNK_SIZE_CHARS", file?.ChunkSizeChars?.ToString(), "800"), "CHUNK_SIZE_CHARS");
        var overlap = ParseNonNegativeInt(GetValue("CHUNK_OVERLAP_CHARS", file?.ChunkOverlapChars?.ToString(), "120"), "CHUNK_OVERLAP_CHARS");
        var minChunk = ParsePositiveInt(GetValue("MIN_CHUNK_CHARS", file?.MinChunkChars?.ToString(), "200"), "MIN_CHUNK_CHARS");
        var maxChunk = ParsePositiveInt(GetValue("MAX_CHUNK_CHARS", file?.MaxChunkChars?.ToString(), "1200"), "MAX_CHUNK_CHARS");
        var timeout = ParsePositiveInt(GetValue("HTTP_TIMEOUT_SECONDS", file?.HttpTimeoutSeconds?.ToString(), "30"), "HTTP_TIMEOUT_SECONDS");

        return new AppConfig
        {
            OllamaBaseUrl = GetValue("OLLAMA_BASE_URL", file?.OllamaBaseUrl, "http://localhost:11434"),
            OllamaModel = GetValue("OLLAMA_MODEL", file?.OllamaModel, "bge-m3"),
            QdrantBaseUrl = GetValue("QDRANT_BASE_URL", file?.QdrantBaseUrl, "http://localhost:6333"),
            QdrantCollection = GetValue("QDRANT_COLLECTION", file?.QdrantCollection, "exp03_chunks"),
            TopK = topK,
            ChunkStrategy = strategy,
            ChunkSizeChars = chunkSize,
            ChunkOverlapChars = overlap,
            MinChunkChars = minChunk,
            MaxChunkChars = maxChunk,
            HttpTimeoutSeconds = timeout
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

    private sealed class AppSettingsFile
    {
        public string? OllamaBaseUrl { get; init; }
        public string? OllamaModel { get; init; }
        public string? QdrantBaseUrl { get; init; }
        public string? QdrantCollection { get; init; }
        public int? TopK { get; init; }
        public string? ChunkStrategy { get; init; }
        public int? ChunkSizeChars { get; init; }
        public int? ChunkOverlapChars { get; init; }
        public int? MinChunkChars { get; init; }
        public int? MaxChunkChars { get; init; }
        public int? HttpTimeoutSeconds { get; init; }
    }
}
