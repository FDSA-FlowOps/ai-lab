using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAIChatClient = OpenAI.Chat.ChatClient;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

const string defaultSpec = """
Build a new public API for subscription management in a SaaS platform.
Requirements:
- API endpoints for create/update/cancel subscription.
- Handle idempotency keys for write operations.
- P95 latency < 250ms.
- SOC2-friendly audit trail and secure defaults.
- Zero-downtime rollout with rollback plan.
""";

var parsed = ParseArgs(args);
var specArg = parsed.Spec;
var printJson = parsed.PrintJson;

var ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")?.Trim();
if (string.IsNullOrWhiteSpace(ollamaBaseUrl))
{
    ollamaBaseUrl = "http://localhost:11434/v1";
}

var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL")?.Trim();
if (string.IsNullOrWhiteSpace(ollamaModel))
{
    ollamaModel = "llama3.2";
}

var spec = string.IsNullOrWhiteSpace(specArg) ? defaultSpec : specArg.Trim();

if (!await CanConnectToOllamaAsync(ollamaBaseUrl))
{
    Console.Error.WriteLine($"No se puede conectar a Ollama en {ollamaBaseUrl}. ¿Está Ollama arrancado?");
    return 1;
}

try
{
    Console.WriteLine($"[INFO] Using Ollama model '{ollamaModel}' at {ollamaBaseUrl}");
    Console.WriteLine("[INFO] Running Architecture Council concurrent analysis...");

    var councilNotes = await RunCouncilConcurrentAsync(ollamaBaseUrl, spec, ollamaModel);

    Console.WriteLine("[INFO] Running Writer/Reviewer group chat (4 iterations)...");

    var finalPlan = await RunWriterReviewerAsync(ollamaBaseUrl, spec, councilNotes, ollamaModel);

    Console.WriteLine();
    Console.WriteLine("===== FINAL ARCHITECTURE COUNCIL PLAN =====");
    Console.WriteLine(finalPlan.Trim());

    if (printJson)
    {
        var json = JsonSerializer.Serialize(new
        {
            spec,
            ollamaBaseUrl,
            ollamaModel,
            councilNotes,
            finalPlan,
        }, new JsonSerializerOptions { WriteIndented = true });

        Console.WriteLine();
        Console.WriteLine("===== JSON =====");
        Console.WriteLine(json);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    return 1;
}

return 0;

static IChatClient BuildChatClient(string baseUrl, string model)
{
    var options = new OpenAIClientOptions
    {
        Endpoint = new Uri(baseUrl),
    };

    var openAiChatClient = new OpenAIChatClient(model, new ApiKeyCredential("ollama"), options);
    return openAiChatClient.AsIChatClient();
}

static async Task<bool> CanConnectToOllamaAsync(string baseUrl)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var modelsEndpoint = baseUrl.TrimEnd('/') + "/models";
        using var response = await http.GetAsync(modelsEndpoint);
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static ChatClientAgent MakeAgent(IChatClient client, string name, string instructions, string description)
{
    return new ChatClientAgent(
        chatClient: client,
        instructions: instructions,
        name: name,
        description: description,
        tools: null,
        loggerFactory: null,
        services: null);
}

static async Task<string> RunCouncilConcurrentAsync(string baseUrl, string spec, string model)
{
    using var architectClient = BuildChatClient(baseUrl, model);
    using var securityClient = BuildChatClient(baseUrl, model);
    using var sreClient = BuildChatClient(baseUrl, model);
    using var pmClient = BuildChatClient(baseUrl, model);
    using var devilClient = BuildChatClient(baseUrl, model);

    var architect = MakeAgent(
        architectClient,
        "Architect",
        "You are the system architect. Focus on architecture shape, boundaries, dependencies, data model, and trade-offs. Keep it concrete.",
        "System architecture lead");

    var security = MakeAgent(
        securityClient,
        "Security",
        "You are the security engineer. Focus on authn/authz, data protection, secrets, abuse prevention, and compliance implications.",
        "Security specialist");

    var sre = MakeAgent(
        sreClient,
        "SRE",
        "You are the SRE. Focus on reliability, SLOs, rollback, failure domains, observability, and operability.",
        "Reliability engineer");

    var pm = MakeAgent(
        pmClient,
        "PM",
        "You are the product manager. Focus on scope, user impact, sequencing, non-goals, and measurable outcomes.",
        "Product manager");

    var devilsAdvocate = MakeAgent(
        devilClient,
        "Devil's Advocate",
        "You are Devil's Advocate. Challenge assumptions aggressively. Output only risks, edge cases, and uncomfortable questions. Include sections: 'How this fails in production' and 'Abuse cases'. Do not propose pretty solutions without a risk justification.",
        "Critical risk challenger");

    var councilAgents = new[] { architect, security, sre, pm, devilsAdvocate };
    var roleOrder = new[] { "Architect", "Security", "SRE", "PM", "Devil's Advocate" };

    var workflow = AgentWorkflowBuilder.BuildConcurrent(
        "arch-council-concurrent",
        councilAgents,
        aggregator: perAgentMessages =>
        {
            var sb = new StringBuilder();

            for (var i = 0; i < perAgentMessages.Count; i++)
            {
                var role = i < roleOrder.Length ? roleOrder[i] : $"Agent-{i + 1}";
                var content = perAgentMessages[i]
                    .Select(m => m.Text?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (content.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"## {role}");
                foreach (var text in content.Distinct(StringComparer.Ordinal))
                {
                    sb.AppendLine(text);
                }
                sb.AppendLine();
            }

            var notes = sb.ToString().Trim();
            return
            [
                new AiChatMessage(
                    ChatRole.Assistant,
                    string.IsNullOrWhiteSpace(notes) ? "No council notes generated." : notes)
            ];
        });

    var input = new List<AiChatMessage>
    {
        new(ChatRole.User, BuildCouncilPrompt(spec)),
    };

    var run = await InProcessExecution.RunAsync(workflow, input);
    EnsureRunSucceeded(run, model);

    var notes = CollectLatestWorkflowText(run);
    if (string.IsNullOrWhiteSpace(notes))
    {
        throw new InvalidOperationException("Concurrent workflow did not produce notes.");
    }

    return notes;
}

static async Task<string> RunWriterReviewerAsync(string baseUrl, string spec, string councilNotes, string model)
{
    using var writerClient = BuildChatClient(baseUrl, model);
    using var reviewerClient = BuildChatClient(baseUrl, model);

    var writer = MakeAgent(writerClient,
        "Writer",
        "You write the final plan. Obey exact output format and include concrete detail. Keep concise but complete.",
        "Plan writer");

    var reviewer = MakeAgent(reviewerClient,
        "Reviewer",
        "You are a strict reviewer. Detect gaps and force improvements. Explicitly check rollout/rollback, observability, security, and idempotency before approving.",
        "Plan reviewer");

    var groupBuilder = AgentWorkflowBuilder.CreateGroupChatBuilderWith(participants =>
    {
        return new RoundRobinGroupChatManager(participants, shouldTerminateFunc: null)
        {
            MaximumIterationCount = 4,
        };
    });

    groupBuilder.AddParticipants(new[] { writer, reviewer });
    var workflow = groupBuilder.Build();

    var input = new List<AiChatMessage>
    {
        new(ChatRole.User, BuildWriterReviewerPrompt(spec, councilNotes)),
    };

    var outputBuffer = new Dictionary<string, string>(StringComparer.Ordinal);
    var lastUsefulMessage = string.Empty;
    var streamingErrors = new List<string>();

    await using (var streamingRun = await InProcessExecution.StreamAsync(workflow, input))
    {
        await foreach (var evt in streamingRun.WatchStreamAsync())
        {
            if (evt is WorkflowErrorEvent workflowError && workflowError.Exception is not null)
            {
                streamingErrors.Add(workflowError.Exception.Message);
                continue;
            }

            if (evt is ExecutorFailedEvent executorFailed && executorFailed.Data is not null)
            {
                streamingErrors.Add(executorFailed.Data.Message);
                continue;
            }

            if (evt is ExecutorCompletedEvent completedEvent)
            {
                Console.WriteLine($"[stream:{completedEvent.ExecutorId}] completed");
            }

            if (evt is not WorkflowOutputEvent outputEvent)
            {
                var fromAny = ExtractStreamEventText(evt);
                if (!string.IsNullOrWhiteSpace(fromAny))
                {
                    lastUsefulMessage = fromAny;
                }
                continue;
            }

            var source = string.IsNullOrWhiteSpace(outputEvent.SourceId) ? "group-chat" : outputEvent.SourceId;
            var emitted = ExtractOutputText(outputEvent);
            if (string.IsNullOrWhiteSpace(emitted))
            {
                continue;
            }

            if (outputBuffer.TryGetValue(source, out var seen) && string.Equals(seen, emitted, StringComparison.Ordinal))
            {
                continue;
            }

            outputBuffer[source] = emitted;
            lastUsefulMessage = emitted;
            Console.WriteLine($"[stream:{source}] {ShortLine(emitted, 280)}");
        }

        var status = await streamingRun.GetStatusAsync();
        if (status != RunStatus.Ended && status != RunStatus.Idle)
        {
            streamingErrors.Add($"unexpected status: {status}");
        }
    }

    if (streamingErrors.Count > 0)
    {
        throw new InvalidOperationException($"Streaming group chat failed: {string.Join(" | ", streamingErrors.Distinct())}");
    }

    if (!string.IsNullOrWhiteSpace(lastUsefulMessage))
    {
        return lastUsefulMessage;
    }

    // Fallback to non-streaming if provider/framework emitted no WorkflowOutput events.
    var run = await InProcessExecution.RunAsync(workflow, input);
    EnsureRunSucceeded(run, model);
    var fallback = CollectLatestWorkflowText(run);
    if (string.IsNullOrWhiteSpace(fallback))
    {
        throw new InvalidOperationException("Group chat workflow did not produce final plan.");
    }

    return fallback;
}

static string CollectLatestWorkflowText(Run run)
{
    string? latest = null;

    foreach (var evt in run.OutgoingEvents)
    {
        if (evt is not WorkflowOutputEvent outputEvent)
        {
            continue;
        }

        if (outputEvent.Is<AgentResponse>(out var response) && !string.IsNullOrWhiteSpace(response?.Text))
        {
            latest = response.Text.Trim();
        }

        if (outputEvent.Is<List<AiChatMessage>>(out var messages) && messages is not null)
        {
            foreach (var msg in messages)
            {
                if (!string.IsNullOrWhiteSpace(msg.Text))
                {
                    latest = msg.Text.Trim();
                }
            }
        }

        if (outputEvent.Is<AiChatMessage>(out var singleMessage) && !string.IsNullOrWhiteSpace(singleMessage?.Text))
        {
            latest = singleMessage.Text.Trim();
        }
    }

    if (string.IsNullOrWhiteSpace(latest))
    {
        throw new InvalidOperationException("No latest workflow text output found.");
    }

    return latest;
}

static string? ExtractOutputText(WorkflowOutputEvent outputEvent)
{
    if (outputEvent.Is<AgentResponse>(out var response) && !string.IsNullOrWhiteSpace(response?.Text))
    {
        return response.Text.Trim();
    }

    if (outputEvent.Is<List<AiChatMessage>>(out var messages) && messages is not null)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
                sb.AppendLine(msg.Text.Trim());
            }
        }

        var text = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
    }

    if (outputEvent.Is<AiChatMessage>(out var singleMessage) && !string.IsNullOrWhiteSpace(singleMessage?.Text))
    {
        return singleMessage.Text.Trim();
    }

    if (outputEvent.Is<string>(out var textContent) && !string.IsNullOrWhiteSpace(textContent))
    {
        return textContent.Trim();
    }

    return null;
}

static string? ExtractStreamEventText(WorkflowEvent evt)
{
    if (evt is ExecutorEvent executorEvent)
    {
        if (executorEvent.Data is AgentResponse response && !string.IsNullOrWhiteSpace(response.Text))
        {
            return response.Text.Trim();
        }

        if (executorEvent.Data is AgentResponseUpdate update && !string.IsNullOrWhiteSpace(update.Text))
        {
            return update.Text.Trim();
        }

        if (executorEvent.Data is AiChatMessage chatMessage && !string.IsNullOrWhiteSpace(chatMessage.Text))
        {
            return chatMessage.Text.Trim();
        }

        if (executorEvent.Data is IEnumerable<AiChatMessage> chatMessages)
        {
            var sb = new StringBuilder();
            foreach (var msg in chatMessages)
            {
                if (!string.IsNullOrWhiteSpace(msg.Text))
                {
                    sb.AppendLine(msg.Text.Trim());
                }
            }

            var text = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
    }

    return null;
}

static string ShortLine(string text, int max)
{
    var oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
    if (oneLine.Length <= max)
    {
        return oneLine;
    }

    return oneLine[..max] + "...";
}

static void EnsureRunSucceeded(Run run, string model)
{
    var errors = new List<string>();

    foreach (var evt in run.OutgoingEvents)
    {
        if (evt is WorkflowErrorEvent workflowError && workflowError.Exception is not null)
        {
            errors.Add(workflowError.Exception.Message);
        }
        else if (evt is ExecutorFailedEvent executorFailed && executorFailed.Data is not null)
        {
            errors.Add(executorFailed.Data.Message);
        }
    }

    if (errors.Count == 0)
    {
        return;
    }

    var joined = string.Join(" | ", errors.Distinct(StringComparer.OrdinalIgnoreCase));
    var modelMissing =
        joined.Contains("model", StringComparison.OrdinalIgnoreCase) &&
        joined.Contains("not found", StringComparison.OrdinalIgnoreCase);

    if (modelMissing)
    {
        throw new InvalidOperationException(
            $"Modelo de chat '{model}' no encontrado en Ollama. Ejecuta: ollama pull {model}");
    }

    throw new InvalidOperationException($"Workflow failed: {joined}");
}

static string BuildCouncilPrompt(string spec)
{
    return $"""
Analyze this architecture spec from your role perspective.
Return concise notes with:
- Findings
- Decisions you recommend
- Risks
- Open questions

SPEC:
{spec}
""";
}

static string BuildWriterReviewerPrompt(string spec, string councilNotes)
{
    return $"""
You are in an Architecture Council writer/reviewer loop.
Use the council notes and produce one final answer with the exact required structure.

Output sections (mandatory):
1) Executive summary (3 bullets)
2) Key decisions (max 5) with trade-offs (pros/cons brief)
3) Risks & mitigations (must include at least 2 risks raised by Devil's Advocate)
4) Rollout plan (phased) + rollback trigger
5) Definition of Done checklist (minimum 10 items, including observability and security)

Style rules:
- Be concrete and implementation-ready.
- No fluff.
- Mention idempotency explicitly.

SPEC:
{spec}

Council notes:
{councilNotes}
""";
}

static (string? Spec, bool PrintJson) ParseArgs(string[] args)
{
    string? spec = null;
    var printJson = false;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
        {
            printJson = true;
            continue;
        }

        if (arg.Equals("--spec", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for --spec.");
            }

            spec = args[i + 1];
            i++;
            continue;
        }
    }

    return (spec, printJson);
}
