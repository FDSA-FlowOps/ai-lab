using System.Text.Json;
using System.Text.Json.Serialization;
using Exp02VectorDbQdrant.Ollama;
using Exp02VectorDbQdrant.Qdrant;

try
{
    var experimentRoot = ResolveExperimentRoot();
    var config = AppConfig.Load(experimentRoot);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    using var ollama = new OllamaEmbeddingClient(config.OllamaBaseUrl, config.HttpTimeoutSeconds);
    using var qdrant = new QdrantClient(config.QdrantBaseUrl, config.HttpTimeoutSeconds);
    var command = CliCommand.Parse(args);
    if (command is null)
    {
        return await RunInteractiveAsync(config, ollama, qdrant, experimentRoot, cts.Token);
    }

    switch (command.Command)
    {
        case "reset":
            return await RunResetAsync(config, ollama, qdrant, experimentRoot, cts.Token);
        case "ingest":
            return await RunIngestAsync(config, ollama, qdrant, experimentRoot, cts.Token);
        case "query":
            return await RunQueryAsync(config, ollama, qdrant, command.Text!, cts.Token);
        default:
            PrintUsage();
            return 1;
    }
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

static async Task<int> RunResetAsync(
    AppConfig config,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    string experimentRoot,
    CancellationToken cancellationToken)
{
    var docs = await LoadDocumentsAsync(experimentRoot);
    if (docs.Count == 0)
    {
        Console.Error.WriteLine("[ERROR] No hay documentos para calcular dimension inicial.");
        return 1;
    }

    Console.WriteLine("[INFO] Calculando embedding inicial para detectar dimension...");
    var probe = await ollama.GetEmbeddingAsync(config.OllamaModel, docs[0].Text, cancellationToken);
    Console.WriteLine($"[INFO] Dimension detectada: {probe.Length}");

    await qdrant.DeleteCollectionIfExistsAsync(config.QdrantCollection, cancellationToken);
    await qdrant.CreateCollectionAsync(config.QdrantCollection, probe.Length, cancellationToken);
    Console.WriteLine($"[OK] Coleccion '{config.QdrantCollection}' recreada.");
    return 0;
}

static async Task<int> RunInteractiveAsync(
    AppConfig config,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    string experimentRoot,
    CancellationToken cancellationToken)
{
    PrintInteractiveHeader(config);

    while (!cancellationToken.IsCancellationRequested)
    {
        Console.Write("qdrant> ");
        var input = Console.ReadLine();
        if (input is null)
        {
            Console.WriteLine();
            return 0;
        }

        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            continue;
        }

        if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            continue;
        }

        var cmd = CliCommand.ParseInteractive(trimmed);
        if (cmd is null)
        {
            Console.WriteLine("[WARN] Comando no reconocido. Usa 'help'.");
            continue;
        }

        try
        {
            switch (cmd.Command)
            {
                case "reset":
                    _ = await RunResetAsync(config, ollama, qdrant, experimentRoot, cancellationToken);
                    break;
                case "ingest":
                    _ = await RunIngestAsync(config, ollama, qdrant, experimentRoot, cancellationToken);
                    break;
                case "query":
                    _ = await RunQueryAsync(config, ollama, qdrant, cmd.Text!, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
        }

        Console.WriteLine();
    }

    return 1;
}

static async Task<int> RunIngestAsync(
    AppConfig config,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    string experimentRoot,
    CancellationToken cancellationToken)
{
    const int embedBatchSize = 16;

    var docs = await LoadDocumentsAsync(experimentRoot);
    if (docs.Count == 0)
    {
        Console.Error.WriteLine("[ERROR] No hay documentos en ./data para ingestar.");
        return 1;
    }

    Console.WriteLine($"[INFO] Cargando embeddings batch para {docs.Count} documentos...");
    var firstBatchCount = Math.Min(embedBatchSize, docs.Count);
    var firstBatchTexts = docs.Take(firstBatchCount).Select(d => d.Text).ToArray();
    var firstBatchEmbeddings = await ollama.GetEmbeddingsAsync(config.OllamaModel, firstBatchTexts, cancellationToken);
    if (firstBatchEmbeddings.Length != firstBatchCount)
    {
        throw new InvalidOperationException(
            $"Ollama devolvio {firstBatchEmbeddings.Length} embeddings en primer batch; esperado {firstBatchCount}.");
    }

    var firstEmbedding = firstBatchEmbeddings[0];
    await qdrant.EnsureCollectionAsync(config.QdrantCollection, firstEmbedding.Length, cancellationToken);

    var points = new List<QdrantPoint>(docs.Count);
    for (var i = 0; i < firstBatchCount; i++)
    {
        points.Add(ToPoint(i, docs[i], firstBatchEmbeddings[i]));
    }

    for (var start = firstBatchCount; start < docs.Count; start += embedBatchSize)
    {
        var count = Math.Min(embedBatchSize, docs.Count - start);
        Console.WriteLine($"[INFO] Embedding docs {start + 1}-{start + count}/{docs.Count}");

        var texts = docs.Skip(start).Take(count).Select(d => d.Text).ToArray();
        var embeddings = await ollama.GetEmbeddingsAsync(config.OllamaModel, texts, cancellationToken);
        if (embeddings.Length != count)
        {
            throw new InvalidOperationException(
                $"Ollama devolvio {embeddings.Length} embeddings en batch; esperado {count}.");
        }

        for (var i = 0; i < count; i++)
        {
            points.Add(ToPoint(start + i, docs[start + i], embeddings[i]));
        }
    }

    Console.WriteLine("[INFO] Enviando points a Qdrant...");
    await qdrant.UpsertPointsAsync(config.QdrantCollection, points, cancellationToken);
    Console.WriteLine($"[OK] Ingest completo. {points.Count} docs en '{config.QdrantCollection}'.");
    return 0;
}

static async Task<int> RunQueryAsync(
    AppConfig config,
    OllamaEmbeddingClient ollama,
    QdrantClient qdrant,
    string query,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("[ERROR] Debes enviar texto para query.");
        return 1;
    }

    Console.WriteLine($"[INFO] Generando embedding para query con modelo '{config.OllamaModel}'...");
    var vector = await ollama.GetEmbeddingAsync(config.OllamaModel, query, cancellationToken);
    var results = await qdrant.SearchAsync(config.QdrantCollection, vector, config.TopK, cancellationToken);

    Console.WriteLine();
    Console.WriteLine($"Top {config.TopK} resultados para: \"{query}\"");
    if (results.Count == 0)
    {
        Console.WriteLine("No se encontraron resultados.");
        return 0;
    }

    for (var i = 0; i < results.Count; i++)
    {
        var r = results[i];
        Console.WriteLine($"{i + 1}. score={r.Score:0.0000} | doc_id={r.DocId} | title={r.Title}");
        Console.WriteLine($"   snippet: {TrimSnippet(r.Snippet, 120)}");
    }

    return 0;
}

static QdrantPoint ToPoint(int index, SupportDocument doc, float[] vector)
{
    return new QdrantPoint
    {
        Id = index + 1,
        Vector = vector,
        Payload = new Dictionary<string, object?>
        {
            ["doc_id"] = doc.DocId,
            ["title"] = doc.Title,
            ["source"] = doc.Source,
            ["text"] = doc.Text,
            ["snippet"] = TrimSnippet(doc.Text, 220),
            ["tags"] = doc.Tags
        }
    };
}

static async Task<List<SupportDocument>> LoadDocumentsAsync(string experimentRoot)
{
    SeedDataFolderIfNeeded(experimentRoot);
    var dataDir = Path.Combine(experimentRoot, "data");
    if (!Directory.Exists(dataDir))
    {
        throw new InvalidOperationException(
            $"No existe carpeta ./data en '{experimentRoot}'.");
    }

    var files = Directory.GetFiles(dataDir, "*.jsonl", SearchOption.TopDirectoryOnly)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (files.Count == 0)
    {
        throw new InvalidOperationException("No se encontraron archivos .jsonl en ./data.");
    }

    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var docs = new List<SupportDocument>();
    foreach (var file in files)
    {
        var lines = await File.ReadAllLinesAsync(file);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SupportDocument? doc;
            try
            {
                doc = JsonSerializer.Deserialize<SupportDocument>(line, options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"JSON invalido en '{file}'.", ex);
            }

            if (doc is null ||
                string.IsNullOrWhiteSpace(doc.DocId) ||
                string.IsNullOrWhiteSpace(doc.Title) ||
                string.IsNullOrWhiteSpace(doc.Source) ||
                string.IsNullOrWhiteSpace(doc.Text))
            {
                throw new InvalidOperationException($"Documento invalido en '{file}'.");
            }

            docs.Add(doc);
        }
    }

    return docs;
}

static void SeedDataFolderIfNeeded(string experimentRoot)
{
    var dataPath = Path.Combine(experimentRoot, "data");
    if (Directory.Exists(dataPath) &&
        Directory.GetFiles(dataPath, "*.jsonl", SearchOption.TopDirectoryOnly).Length > 0)
    {
        return;
    }

    var samplePath = Path.Combine(experimentRoot, "Data", "Samples", "docs.jsonl");
    if (!File.Exists(samplePath))
    {
        samplePath = Path.Combine(AppContext.BaseDirectory, "Data", "Samples", "docs.jsonl");
    }

    if (!File.Exists(samplePath))
    {
        return;
    }

    Directory.CreateDirectory(dataPath);
    var target = Path.Combine(dataPath, "docs.jsonl");
    File.Copy(samplePath, target, overwrite: true);
    Console.WriteLine($"[INFO] Seed inicial creado en ./data desde '{samplePath}'.");
}

static string TrimSnippet(string text, int maxLen)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return string.Empty;
    }

    var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
    return clean.Length <= maxLen ? clean : clean[..maxLen] + "...";
}

static void PrintUsage()
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run");
    Console.WriteLine("  dotnet run -- ingest");
    Console.WriteLine("  dotnet run -- query \"texto\"");
    Console.WriteLine("  dotnet run -- reset");
    Console.WriteLine("  En modo interactivo: ingest | query <texto> | reset | help | exit");
}

static void PrintInteractiveHeader(AppConfig config)
{
    Console.WriteLine("Modo interactivo activado.");
    Console.WriteLine($"Coleccion: {config.QdrantCollection}");
    Console.WriteLine($"Qdrant: {config.QdrantBaseUrl}");
    Console.WriteLine($"Ollama: {config.OllamaBaseUrl} ({config.OllamaModel})");
    Console.WriteLine($"TOP_K: {config.TopK}");
    Console.WriteLine("Comandos: ingest | query <texto> | reset | help | exit");
    Console.WriteLine();
}

static string ResolveExperimentRoot()
{
    var candidates = new List<string>
    {
        Directory.GetCurrentDirectory(),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
        AppContext.BaseDirectory
    };

    foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var sample = Path.Combine(candidate, "Data", "Samples", "docs.jsonl");
        if (File.Exists(sample))
        {
            return candidate;
        }
    }

    return Directory.GetCurrentDirectory();
}

sealed record CliCommand(string Command, string? Text)
{
    public static CliCommand? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var cmd = args[0].Trim().ToLowerInvariant();
        return cmd switch
        {
            "ingest" => new CliCommand("ingest", null),
            "reset" => new CliCommand("reset", null),
            "query" when args.Length >= 2 => new CliCommand("query", string.Join(' ', args.Skip(1))),
            _ => null
        };
    }

    public static CliCommand? ParseInteractive(string input)
    {
        var cmd = input.Trim();
        if (cmd.Equals("ingest", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommand("ingest", null);
        }

        if (cmd.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommand("reset", null);
        }

        if (cmd.StartsWith("query ", StringComparison.OrdinalIgnoreCase))
        {
            var text = cmd[6..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(text) ? null : new CliCommand("query", text);
        }

        return null;
    }
}

sealed class AppConfig
{
    public required string OllamaBaseUrl { get; init; }
    public required string OllamaModel { get; init; }
    public required string QdrantBaseUrl { get; init; }
    public required string QdrantCollection { get; init; }
    public required int TopK { get; init; }
    public required int HttpTimeoutSeconds { get; init; }

    public static AppConfig Load(string experimentRoot)
    {
        var appSettingsPath = Path.Combine(experimentRoot, "appsettings.json");
        var appSettings = File.Exists(appSettingsPath)
            ? JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText(appSettingsPath))
            : null;

        var topKRaw = GetValue("TOP_K", appSettings?.TopK?.ToString(), "5");
        var timeoutRaw = GetValue("HTTP_TIMEOUT_SECONDS", appSettings?.HttpTimeoutSeconds?.ToString(), "30");

        if (!int.TryParse(topKRaw, out var topK) || topK <= 0)
        {
            throw new InvalidOperationException($"TOP_K invalido: '{topKRaw}'.");
        }

        if (!int.TryParse(timeoutRaw, out var timeout) || timeout <= 0)
        {
            throw new InvalidOperationException($"HTTP_TIMEOUT_SECONDS invalido: '{timeoutRaw}'.");
        }

        return new AppConfig
        {
            OllamaBaseUrl = GetValue("OLLAMA_BASE_URL", appSettings?.OllamaBaseUrl, "http://localhost:11434"),
            OllamaModel = GetValue("OLLAMA_MODEL", appSettings?.OllamaModel, "bge-m3"),
            QdrantBaseUrl = GetValue("QDRANT_BASE_URL", appSettings?.QdrantBaseUrl, "http://localhost:6333"),
            QdrantCollection = GetValue("QDRANT_COLLECTION", appSettings?.QdrantCollection, "exp02_docs"),
            TopK = topK,
            HttpTimeoutSeconds = timeout
        };
    }

    private static string GetValue(string envKey, string? appSettingsValue, string defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        if (!string.IsNullOrWhiteSpace(appSettingsValue))
        {
            return appSettingsValue;
        }

        return defaultValue;
    }

    private sealed class AppSettingsFile
    {
        public string? OllamaBaseUrl { get; init; }
        public string? OllamaModel { get; init; }
        public string? QdrantBaseUrl { get; init; }
        public string? QdrantCollection { get; init; }
        public int? TopK { get; init; }
        public int? HttpTimeoutSeconds { get; init; }
    }
}

sealed class SupportDocument
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string[] Tags { get; init; } = [];
}
