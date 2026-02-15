using System.Globalization;
using System.Text.Json;
using Exp01EmbeddingsOllama.Data;
using Exp01EmbeddingsOllama.Math;
using Exp01EmbeddingsOllama.Ollama;

try
{
    var appConfig = AppConfig.Load();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    using var client = new OllamaEmbeddingClient(
        appConfig.OllamaBaseUrl,
        appConfig.HttpTimeoutSeconds);

    var cli = CliCommand.Parse(args);
    if (cli is not null)
    {
        return await ExecuteCommandAsync(cli, client, appConfig, cts.Token);
    }

    return await RunInteractiveAsync(client, appConfig, cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("[ERROR] Operacion cancelada.");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    return 1;
}

static async Task<int> ExecuteCommandAsync(
    CliCommand cli,
    OllamaEmbeddingClient client,
    AppConfig config,
    CancellationToken cancellationToken)
{
    switch (cli.Command)
    {
        case "embed":
            return await RunEmbedAsync(client, config.OllamaModel, cli.Text!, cancellationToken);
        case "similar":
            return await RunSimilarAsync(client, config, cli.Text!, cli.ThresholdOnly, cancellationToken);
        case "duplicates":
            return await RunDuplicatesAsync(client, config, cancellationToken);
        default:
            PrintUsage();
            return 1;
    }
}

static async Task<int> RunInteractiveAsync(
    OllamaEmbeddingClient client,
    AppConfig config,
    CancellationToken cancellationToken)
{
    PrintInteractiveHeader(config);

    while (!cancellationToken.IsCancellationRequested)
    {
        Console.Write("rag> ");
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

        var command = CliCommand.ParseInteractive(trimmed);
        if (command is null)
        {
            Console.WriteLine("[WARN] Comando no reconocido. Usa 'help' para ver opciones.");
            continue;
        }

        try
        {
            _ = await ExecuteCommandAsync(command, client, config, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
        }

        Console.WriteLine();
    }

    return 1;
}

static async Task<int> RunEmbedAsync(
    OllamaEmbeddingClient client,
    string model,
    string text,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.Error.WriteLine("[ERROR] Debes enviar texto para 'embed'.");
        return 1;
    }

    var embedding = await client.GetEmbeddingAsync(model, text, cancellationToken);
    Console.WriteLine($"Model: {model}");
    Console.WriteLine($"Dimension: {embedding.Length}");
    Console.WriteLine("Preview (primeros 8 valores):");
    Console.WriteLine(string.Join(", ", embedding.Take(8).Select(x => x.ToString("0.000000"))));
    return 0;
}

static async Task<int> RunSimilarAsync(
    OllamaEmbeddingClient client,
    AppConfig config,
    string query,
    bool thresholdOnly,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("[ERROR] Debes enviar texto para 'similar'.");
        return 1;
    }

    Console.WriteLine($"[INFO] Generando embedding para query con modelo '{config.OllamaModel}'...");
    var queryEmbedding = Cosine.Normalize(await client.GetEmbeddingAsync(config.OllamaModel, query, cancellationToken));

    var sampleEmbeddings = new List<(string Text, float[] Embedding)>(Samples.All.Count);
    for (var i = 0; i < Samples.All.Count; i++)
    {
        var sample = Samples.All[i];
        Console.WriteLine($"[INFO] Embedding sample {i + 1}/{Samples.All.Count}");
        var emb = await client.GetEmbeddingAsync(config.OllamaModel, sample, cancellationToken);
        sampleEmbeddings.Add((sample, Cosine.Normalize(emb)));
    }

    var ranked = sampleEmbeddings
        .Select(x => new
        {
            x.Text,
            Score = Cosine.Dot(queryEmbedding, x.Embedding)
        })
        .OrderByDescending(x => x.Score);

    var filtered = thresholdOnly
        ? ranked.Where(x => x.Score >= config.DupThreshold)
        : ranked;

    var top = filtered
        .Take(config.TopK)
        .ToList();

    Console.WriteLine();
    var mode = thresholdOnly
        ? $"Top {config.TopK} similares (score >= {config.DupThreshold:0.00})"
        : $"Top {config.TopK} similares";
    Console.WriteLine($"{mode} para: \"{query}\"");
    if (top.Count == 0)
    {
        Console.WriteLine("No hubo resultados que superen el umbral.");
        return 0;
    }

    for (var i = 0; i < top.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {top[i].Score:0.0000} | {top[i].Text}");
    }

    return 0;
}

static async Task<int> RunDuplicatesAsync(
    OllamaEmbeddingClient client,
    AppConfig config,
    CancellationToken cancellationToken)
{
    Console.WriteLine($"[INFO] Calculando embeddings para {Samples.All.Count} frases...");
    var normalized = new List<(int Index, string Text, float[] Embedding)>(Samples.All.Count);
    for (var i = 0; i < Samples.All.Count; i++)
    {
        var text = Samples.All[i];
        Console.WriteLine($"[INFO] Embedding sample {i + 1}/{Samples.All.Count}");
        var emb = await client.GetEmbeddingAsync(config.OllamaModel, text, cancellationToken);
        normalized.Add((i, text, Cosine.Normalize(emb)));
    }

    var duplicates = new List<(int Left, int Right, double Score)>();
    for (var i = 0; i < normalized.Count; i++)
    {
        for (var j = i + 1; j < normalized.Count; j++)
        {
            var score = Cosine.Dot(normalized[i].Embedding, normalized[j].Embedding);
            if (score >= config.DupThreshold)
            {
                duplicates.Add((i, j, score));
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Deteccion de duplicados semanticos (threshold >= {config.DupThreshold:0.00})");
    if (duplicates.Count == 0)
    {
        Console.WriteLine("No se detectaron duplicados semanticos con el umbral actual.");
        return 0;
    }

    foreach (var item in duplicates.OrderByDescending(x => x.Score))
    {
        Console.WriteLine($"[{item.Score:0.0000}]");
        Console.WriteLine($"A: {Samples.All[item.Left]}");
        Console.WriteLine($"B: {Samples.All[item.Right]}");
        Console.WriteLine();
    }

    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run");
    Console.WriteLine("  dotnet run -- embed \"texto\"");
    Console.WriteLine("  dotnet run -- similar \"query texto\"");
    Console.WriteLine("  dotnet run -- similar --threshold-only \"query texto\"");
    Console.WriteLine("  dotnet run -- duplicates");
    Console.WriteLine("  En modo interactivo: embed <texto> | similar <texto> | similar-th <texto> | duplicates | help | exit");
}

static void PrintInteractiveHeader(AppConfig config)
{
    Console.WriteLine("Modo interactivo activado.");
    Console.WriteLine($"Modelo: {config.OllamaModel}");
    Console.WriteLine($"Base URL: {config.OllamaBaseUrl}");
    Console.WriteLine($"TOP_K: {config.TopK} | DUP_THRESHOLD: {config.DupThreshold:0.00}");
    Console.WriteLine("Comandos: embed <texto> | similar <texto> | similar-th <texto> | duplicates | help | exit");
    Console.WriteLine();
}

sealed record CliCommand(string Command, string? Text, bool ThresholdOnly)
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
            "embed" when args.Length >= 2 => new CliCommand(cmd, string.Join(' ', args.Skip(1)), false),
            "similar" when args.Length >= 2 => ParseSimilarArgs(args),
            "duplicates" => new CliCommand(cmd, null, false),
            _ => null
        };
    }

    public static CliCommand? ParseInteractive(string input)
    {
        var cmd = input.Trim();
        if (cmd.Equals("duplicates", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommand("duplicates", null, false);
        }

        if (cmd.StartsWith("embed ", StringComparison.OrdinalIgnoreCase))
        {
            var text = cmd[6..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(text) ? null : new CliCommand("embed", text, false);
        }

        if (cmd.StartsWith("similar ", StringComparison.OrdinalIgnoreCase))
        {
            var text = cmd[8..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(text) ? null : new CliCommand("similar", text, false);
        }

        if (cmd.StartsWith("similar-th ", StringComparison.OrdinalIgnoreCase))
        {
            var text = cmd[11..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(text) ? null : new CliCommand("similar", text, true);
        }

        return null;
    }

    private static CliCommand? ParseSimilarArgs(string[] args)
    {
        if (args.Length >= 3 && args[1].Equals("--threshold-only", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommand("similar", string.Join(' ', args.Skip(2)), true);
        }

        return new CliCommand("similar", string.Join(' ', args.Skip(1)), false);
    }
}

sealed class AppConfig
{
    public required string OllamaBaseUrl { get; init; }
    public required string OllamaModel { get; init; }
    public required double DupThreshold { get; init; }
    public required int TopK { get; init; }
    public required int HttpTimeoutSeconds { get; init; }

    public static AppConfig Load()
    {
        var appSettings = File.Exists("appsettings.json")
            ? JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText("appsettings.json"))
            : null;

        var baseUrl = GetValue("OLLAMA_BASE_URL", appSettings?.OllamaBaseUrl, "http://localhost:11434");
        var model = GetValue("OLLAMA_MODEL", appSettings?.OllamaModel, "bge-m3");
        var thresholdRaw = GetValue("DUP_THRESHOLD", appSettings?.DupThreshold?.ToString(), "0.85");
        var topKRaw = GetValue("TOP_K", appSettings?.TopK?.ToString(), "5");
        var timeoutRaw = GetValue("HTTP_TIMEOUT_SECONDS", appSettings?.HttpTimeoutSeconds?.ToString(), "30");

        if (!TryParseDoubleFlexible(thresholdRaw, out var threshold))
        {
            throw new InvalidOperationException($"DUP_THRESHOLD invalido: '{thresholdRaw}'.");
        }

        if (!int.TryParse(topKRaw, out var topK) || topK <= 0)
        {
            throw new InvalidOperationException($"TOP_K invalido: '{topKRaw}'. Debe ser entero > 0.");
        }

        if (!int.TryParse(timeoutRaw, out var timeoutSeconds) || timeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"HTTP_TIMEOUT_SECONDS invalido: '{timeoutRaw}'. Debe ser entero > 0.");
        }

        return new AppConfig
        {
            OllamaBaseUrl = baseUrl,
            OllamaModel = model,
            DupThreshold = threshold,
            TopK = topK,
            HttpTimeoutSeconds = timeoutSeconds
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

    private static bool TryParseDoubleFlexible(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        var normalized = raw.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed class AppSettingsFile
    {
        public string? OllamaBaseUrl { get; init; }
        public string? OllamaModel { get; init; }
        public double? DupThreshold { get; init; }
        public int? TopK { get; init; }
        public int? HttpTimeoutSeconds { get; init; }
    }
}
