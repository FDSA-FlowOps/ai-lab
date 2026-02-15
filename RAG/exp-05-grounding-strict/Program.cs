using Exp05GroundingStrict.Models;
using Exp05GroundingStrict.Ollama;
using Exp05GroundingStrict.Qdrant;
using Exp05GroundingStrict.Services;

try
{
    var root = ResolveExperimentRoot();
    var config = AppConfig.Load(root);
    var runtime = config.ToRuntimeSettings();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    using var embeddingClient = new OllamaEmbeddingClient(config.OllamaBaseUrl, config.HttpTimeoutSeconds);
    using var chatClient = new OllamaChatClient(config.OllamaBaseUrl, config.HttpTimeoutSeconds);
    using var qdrantClient = new QdrantClient(config.QdrantBaseUrl, config.HttpTimeoutSeconds);

    var ingestion = new IngestionService(root, config, embeddingClient, qdrantClient);
    var retrieval = new RetrievalService(config, embeddingClient, qdrantClient);
    var verifier = new GroundingVerifier();
    var grounded = new GroundedAnswerService(config, retrieval, chatClient, verifier);

    await RunMenuAsync(config, runtime, ingestion, retrieval, grounded, qdrantClient, cts.Token);
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

static async Task RunMenuAsync(
    AppConfig config,
    RuntimeSettings runtime,
    IngestionService ingestion,
    RetrievalService retrieval,
    GroundedAnswerService grounded,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        PrintMenu(config, runtime);
        Console.Write("Selecciona una opcion (1-6): ");
        var option = Console.ReadLine()?.Trim();
        Console.WriteLine();

        try
        {
            switch (option)
            {
                case "1":
                    await ingestion.ResetCollectionAsync(runtime, cancellationToken);
                    break;
                case "2":
                {
                    var stats = await ingestion.IngestAsync(runtime, cancellationToken);
                    Console.WriteLine(
                        $"[OK] Ingest completado: docs={stats.Documents}, chunks={stats.Chunks}, min={stats.MinChars}, avg={stats.AvgChars:0.0}, max={stats.MaxChars}.");
                    break;
                }
                case "3":
                    await AskGroundedAsync(runtime, grounded, cancellationToken);
                    break;
                case "4":
                    await ShowRetrievedChunksAsync(config, runtime, retrieval, qdrant, cancellationToken);
                    break;
                case "5":
                    ChangeRuntimeSettings(runtime);
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

static async Task AskGroundedAsync(
    RuntimeSettings runtime,
    GroundedAnswerService grounded,
    CancellationToken cancellationToken)
{
    Console.Write("Pregunta: ");
    var question = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(question))
    {
        Console.WriteLine("[WARN] Pregunta vacia.");
        return;
    }

    var result = await grounded.AskGroundedAsync(question, runtime, cancellationToken);
    Console.WriteLine($"top1_score={result.Top1Score:0.0000} | gap(top1-top2)={result.GapTop1Top2:0.0000}");
    Console.WriteLine($"minScore={runtime.MinRetrievalScore:0.00} | minGap={runtime.MinRetrievalGap:0.00}");
    if (result.IsAmbiguous)
    {
        Console.WriteLine("[WARN] Ambiguedad de retrieval detectada (gap bajo).");
    }
    Console.WriteLine();
    Console.WriteLine("Respuesta:");
    Console.WriteLine(result.Answer);
    Console.WriteLine();

    if (result.Citations.Count > 0)
    {
        Console.WriteLine($"Citas usadas: {string.Join(", ", result.Citations)}");
    }

    if (!result.IsValidGroundedOutput)
    {
        Console.WriteLine("Sugerencia: prueba aumentar TOP_K o ajustar el presupuesto de contexto.");
    }

    if (runtime.ShowDebug || !result.IsValidGroundedOutput)
    {
        Console.WriteLine();
        Console.WriteLine(result.IsValidGroundedOutput
            ? "Debug - Chunks recuperados:"
            : "Chunks recuperados:");
        for (var i = 0; i < result.Retrieved.Count; i++)
        {
            var r = result.Retrieved[i];
            Console.WriteLine($"{i + 1}. score={r.Score:0.0000} | [{r.DocId}|{r.SectionTitle}|{r.ChunkIndex}]");
            Console.WriteLine($"   {Trim(r.ChunkText, 150)}");
        }
    }
}

static async Task ShowRetrievedChunksAsync(
    AppConfig config,
    RuntimeSettings runtime,
    RetrievalService retrieval,
    QdrantClient qdrant,
    CancellationToken cancellationToken)
{
    var count = await qdrant.CountPointsAsync(config.QdrantCollection, cancellationToken);
    if (count == 0)
    {
        Console.WriteLine("[WARN] La coleccion esta vacia. Ejecuta opcion 2) Ingest documents.");
        return;
    }

    Console.Write("Pregunta para retrieval: ");
    var question = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(question))
    {
        Console.WriteLine("[WARN] Pregunta vacia.");
        return;
    }

    var retrieved = await retrieval.RetrieveAsync(question, runtime.TopK, cancellationToken);
    Console.WriteLine();
    Console.WriteLine($"Top {runtime.TopK} chunks:");
    if (retrieved.Count == 0)
    {
        Console.WriteLine("Sin resultados.");
        return;
    }

    for (var i = 0; i < retrieved.Count; i++)
    {
        var r = retrieved[i];
        Console.WriteLine($"{i + 1}. score={r.Score:0.0000} | [{r.DocId}|{r.SectionTitle}|{r.ChunkIndex}]");
        Console.WriteLine($"   {Trim(r.ChunkText, 180)}");
    }
}

static void ChangeRuntimeSettings(RuntimeSettings runtime)
{
    runtime.TopK = PromptPositiveInt("TopK", runtime.TopK);
    runtime.MinRetrievalScore = PromptDouble("Min retrieval score", runtime.MinRetrievalScore);
    runtime.MinRetrievalGap = PromptDouble("Min retrieval gap", runtime.MinRetrievalGap);
    runtime.MaxContextCharsBudget = PromptPositiveInt("Max context chars budget", runtime.MaxContextCharsBudget);
    runtime.MaxChunkCharsForPrompt = PromptPositiveInt("Max chunk chars for prompt", runtime.MaxChunkCharsForPrompt);
    runtime.ChatTemperature = PromptDouble("Chat temperature", runtime.ChatTemperature);
    runtime.ChatTopP = PromptDouble("Chat top_p", runtime.ChatTopP);
    runtime.ChatNumCtx = PromptPositiveInt("Chat num_ctx", runtime.ChatNumCtx);
    runtime.ShowDebug = PromptBool("Show debug", runtime.ShowDebug);
    runtime.ChunkSizeChars = PromptPositiveInt("Chunk size chars", runtime.ChunkSizeChars);
    runtime.ChunkOverlapChars = PromptNonNegativeInt("Chunk overlap chars", runtime.ChunkOverlapChars);
    runtime.MinChunkChars = PromptPositiveInt("Min chunk chars", runtime.MinChunkChars);

    if (runtime.ChunkOverlapChars >= runtime.ChunkSizeChars)
    {
        Console.WriteLine("[WARN] overlap >= size no permitido. Se ajusta overlap=size-1.");
        runtime.ChunkOverlapChars = runtime.ChunkSizeChars - 1;
    }

    Console.WriteLine("[OK] Settings actualizados para esta sesion.");
}

static int PromptPositiveInt(string label, int current)
{
    Console.Write($"{label} (actual={current}, ENTER para mantener): ");
    var raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return current;
    }

    return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : current;
}

static int PromptNonNegativeInt(string label, int current)
{
    Console.Write($"{label} (actual={current}, ENTER para mantener): ");
    var raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return current;
    }

    return int.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : current;
}

static double PromptDouble(string label, double current)
{
    Console.Write($"{label} (actual={current:0.00}, ENTER para mantener): ");
    var raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return current;
    }

    var normalized = raw.Replace(',', '.');
    return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : current;
}

static bool PromptBool(string label, bool current)
{
    Console.Write($"{label} (actual={current}, ENTER para mantener): ");
    var raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return current;
    }

    return bool.TryParse(raw, out var parsed) ? parsed : current;
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
    Console.WriteLine("=== EXP-05 Grounding Strict ===");
    Console.WriteLine($"Collection: {config.QdrantCollection}");
    Console.WriteLine($"Embed model: {config.OllamaEmbedModel}");
    Console.WriteLine($"Chat model: {config.OllamaChatModel}");
    Console.WriteLine($"topK={runtime.TopK} | minScore={runtime.MinRetrievalScore:0.00} | minGap={runtime.MinRetrievalGap:0.00} | showDebug={runtime.ShowDebug}");
    Console.WriteLine($"chatOptions: temperature={runtime.ChatTemperature:0.00}, top_p={runtime.ChatTopP:0.00}, num_ctx={runtime.ChatNumCtx}");
    Console.WriteLine($"contextBudget={runtime.MaxContextCharsBudget} | chunkCharsForPrompt={runtime.MaxChunkCharsForPrompt}");
    Console.WriteLine($"chunkSize={runtime.ChunkSizeChars} | overlap={runtime.ChunkOverlapChars} | minChunk={runtime.MinChunkChars}");
    Console.WriteLine();
    Console.WriteLine("1) Reset collection");
    Console.WriteLine("2) Ingest documents");
    Console.WriteLine("3) Ask grounded (bullets + citas)");
    Console.WriteLine("4) Show retrieved chunks for a question");
    Console.WriteLine("5) Change runtime settings");
    Console.WriteLine("6) Exit");
    Console.WriteLine();
}

static string Trim(string text, int max)
{
    var clean = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return clean.Length <= max ? clean : clean[..max] + "...";
}

static void TryClearConsole()
{
    try
    {
        Console.Clear();
    }
    catch
    {
    }
}
