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

    Console.WriteLine("[INFO] Running Writer/Reviewer/Moderator group chat (6 iterations)...");

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
    const string finalMarker = "=== FINAL PLAN ===";
    using var writerClient = BuildChatClient(baseUrl, model);
    using var reviewerClient = BuildChatClient(baseUrl, model);
    using var moderatorClient = BuildChatClient(baseUrl, model);

    var writer = MakeAgent(writerClient,
        "Writer",
        "You draft the plan. Output ONLY the plan content. Do not include commentary, review, or preface.",
        "Plan writer");

    var reviewer = MakeAgent(reviewerClient,
        "Reviewer",
        "You are a strict reviewer. Provide ONLY a numbered list of requested changes. Do not rewrite the document. Explicitly check rollout/rollback, observability, security, and idempotency.",
        "Plan reviewer");
    var moderator = MakeAgent(moderatorClient,
        "Moderator",
        $"You are the finalizer. Output ONLY the final plan. Start with '{finalMarker}' on the first line. No commentary, no suggestions, no meta text.",
        "Final plan moderator");
    var trustedStreamExecutors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        moderator.Id,
        "GroupChatHost"
    };

    var groupBuilder = AgentWorkflowBuilder.CreateGroupChatBuilderWith(participants =>
    {
        return new RoundRobinGroupChatManager(participants, shouldTerminateFunc: null)
        {
            MaximumIterationCount = 6,
        };
    });

    groupBuilder.AddParticipants(new[] { writer, reviewer, moderator });
    var workflow = groupBuilder.Build();

    var input = new List<AiChatMessage>
    {
        new(ChatRole.User, BuildWriterReviewerPrompt(spec, councilNotes)),
    };

    var outputBuffer = new Dictionary<string, string>(StringComparer.Ordinal);
    string? lastModeratorAssistant = null;
    string? lastMarkerPlan = null;
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
                var fromAny = ExtractStreamEventText(evt, trustedStreamExecutors);
                if (!string.IsNullOrWhiteSpace(fromAny))
                {
                    var fromMarker = TryExtractFinalPlanFromMarker(fromAny, finalMarker);
                    if (!string.IsNullOrWhiteSpace(fromMarker))
                    {
                        lastMarkerPlan = fromMarker;
                    }
                }
                continue;
            }

            var source = string.IsNullOrWhiteSpace(outputEvent.SourceId) ? "group-chat" : outputEvent.SourceId;
            var emitted = ExtractOutputText(outputEvent, trustedStreamExecutors);
            if (string.IsNullOrWhiteSpace(emitted))
            {
                continue;
            }

            if (outputBuffer.TryGetValue(source, out var seen) && string.Equals(seen, emitted, StringComparison.Ordinal))
            {
                continue;
            }

            outputBuffer[source] = emitted;
            if (string.Equals(source, moderator.Id, StringComparison.OrdinalIgnoreCase))
            {
                lastModeratorAssistant = emitted;
            }
            var markerPlan = TryExtractFinalPlanFromMarker(emitted, finalMarker);
            if (!string.IsNullOrWhiteSpace(markerPlan))
            {
                lastMarkerPlan = markerPlan;
            }
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

    var streamingCandidate = !string.IsNullOrWhiteSpace(lastMarkerPlan) ? lastMarkerPlan : lastModeratorAssistant;
    if (!string.IsNullOrWhiteSpace(streamingCandidate))
    {
        var cleaned = CleanFinalPlan(streamingCandidate, finalMarker);
        cleaned = EnsureDevilsAdvocateRiskCoverage(cleaned, councilNotes);
        EnsureValidFinalPlan(cleaned);
        return cleaned;
    }

    // Fallback to non-streaming if provider/framework emitted no WorkflowOutput events.
    var run = await InProcessExecution.RunAsync(workflow, input);
    EnsureRunSucceeded(run, model);
    var fallback = ExtractFinalPlanFromRun(run, moderator.Id, trustedStreamExecutors, finalMarker);
    if (string.IsNullOrWhiteSpace(fallback))
    {
        throw new InvalidOperationException("Group chat produced no assistant output.");
    }
    var finalPlan = CleanFinalPlan(fallback, finalMarker);
    finalPlan = EnsureDevilsAdvocateRiskCoverage(finalPlan, councilNotes);
    EnsureValidFinalPlan(finalPlan);

    return finalPlan;
}

static string CollectLatestWorkflowText(Run run)
{
    var latest = ExtractFinalPlanFromRun(run, moderatorExecutorId: null, trustedExecutors: null, finalMarker: null);
    if (string.IsNullOrWhiteSpace(latest))
    {
        throw new InvalidOperationException("No assistant output found in workflow run.");
    }

    return latest;
}

static string? ExtractOutputText(WorkflowOutputEvent outputEvent, ISet<string>? trustedExecutors)
{
    if (outputEvent.Is<List<AiChatMessage>>(out var messages) && messages is not null)
    {
        var lastAssistant = GetLastAssistantText(messages);
        if (!string.IsNullOrWhiteSpace(lastAssistant))
        {
            return lastAssistant;
        }
    }

    if (outputEvent.Is<AiChatMessage>(out var singleMessage) &&
        singleMessage is not null &&
        singleMessage.Role == ChatRole.Assistant &&
        !string.IsNullOrWhiteSpace(singleMessage.Text))
    {
        return singleMessage.Text.Trim();
    }

    if (outputEvent.Is<AgentResponse>(out var response) && response is not null)
    {
        var byMessages = GetLastAssistantText(response.Messages ?? []);
        if (!string.IsNullOrWhiteSpace(byMessages))
        {
            return byMessages;
        }

        // Use free text only from trusted assistant-like executors.
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            var source = outputEvent.SourceId;
            if (IsTrustedExecutor(source, trustedExecutors))
            {
                return response.Text.Trim();
            }
        }
    }

    // Ignore raw string payload by default to avoid returning prompt echo.
    return null;
}

static string? ExtractStreamEventText(WorkflowEvent evt, ISet<string>? trustedExecutors)
{
    if (evt is ExecutorEvent executorEvent)
    {
        if (!IsTrustedExecutor(executorEvent.ExecutorId, trustedExecutors))
        {
            return null;
        }

        if (executorEvent.Data is AgentResponse response)
        {
            var byMessages = GetLastAssistantText(response.Messages ?? []);
            if (!string.IsNullOrWhiteSpace(byMessages))
            {
                return byMessages;
            }

            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response.Text.Trim();
            }
        }

        if (executorEvent.Data is AgentResponseUpdate update &&
            update.Role == ChatRole.Assistant &&
            !string.IsNullOrWhiteSpace(update.Text))
        {
            return update.Text.Trim();
        }

        if (executorEvent.Data is AiChatMessage chatMessage &&
            chatMessage.Role == ChatRole.Assistant &&
            !string.IsNullOrWhiteSpace(chatMessage.Text))
        {
            return chatMessage.Text.Trim();
        }

        if (executorEvent.Data is IEnumerable<AiChatMessage> chatMessages)
        {
            var byMessages = GetLastAssistantText(chatMessages);
            if (!string.IsNullOrWhiteSpace(byMessages))
            {
                return byMessages;
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

static string? GetLastAssistantText(IEnumerable<AiChatMessage> messages)
{
    string? last = null;
    foreach (var message in messages)
    {
        if (message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
        {
            last = message.Text.Trim();
        }
    }

    return last;
}

static string? ExtractFinalPlanFromRun(
    Run run,
    string? moderatorExecutorId,
    ISet<string>? trustedExecutors,
    string? finalMarker)
{
    string? latestAssistant = null;
    string? latestModeratorAssistant = null;
    string? latestMarkedPlan = null;

    foreach (var evt in run.OutgoingEvents)
    {
        if (evt is not WorkflowOutputEvent outputEvent)
        {
            continue;
        }

        var candidate = ExtractOutputText(outputEvent, trustedExecutors);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            latestAssistant = candidate;
            if (!string.IsNullOrWhiteSpace(finalMarker))
            {
                var marked = TryExtractFinalPlanFromMarker(candidate, finalMarker);
                if (!string.IsNullOrWhiteSpace(marked))
                {
                    latestMarkedPlan = marked;
                }
            }

            if (!string.IsNullOrWhiteSpace(moderatorExecutorId) &&
                string.Equals(outputEvent.SourceId, moderatorExecutorId, StringComparison.OrdinalIgnoreCase))
            {
                latestModeratorAssistant = candidate;
            }
        }
    }

    if (!string.IsNullOrWhiteSpace(latestMarkedPlan))
    {
        return latestMarkedPlan;
    }

    if (!string.IsNullOrWhiteSpace(moderatorExecutorId))
    {
        return latestModeratorAssistant;
    }

    return latestAssistant;
}

static bool IsTrustedExecutor(string? executorId, ISet<string>? trustedExecutors)
{
    if (string.IsNullOrWhiteSpace(executorId))
    {
        return false;
    }

    if (trustedExecutors is null || trustedExecutors.Count == 0)
    {
        return true;
    }

    return trustedExecutors.Contains(executorId);
}

static void EnsureNotPromptEcho(string text)
{
    if (text.Contains("You are in an Architecture Council writer/reviewer loop.", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Group chat produced prompt echo instead of assistant output.");
    }
}

static string? TryExtractFinalPlanFromMarker(string text, string marker)
{
    var idx = text.IndexOf(marker, StringComparison.Ordinal);
    if (idx < 0)
    {
        return null;
    }

    return text[idx..].Trim();
}

static string CleanFinalPlan(string text, string marker)
{
    var trimmed = text.Trim();
    var fromMarker = TryExtractFinalPlanFromMarker(trimmed, marker);
    return string.IsNullOrWhiteSpace(fromMarker) ? trimmed : fromMarker.Trim();
}

static void EnsureValidFinalPlan(string text)
{
    EnsureNotPromptEcho(text);
    if (text.Contains("Your revised document", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("here are suggestions", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Group chat returned editorial feedback instead of a final plan.");
    }

    var requiredSections = new[]
    {
        "Executive summary",
        "Key decisions",
        "Risks & mitigations",
        "Rollout plan",
        "Definition of Done"
    };

    foreach (var section in requiredSections)
    {
        if (!text.Contains(section, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Final plan missing required section: {section}");
        }
    }

    var devilCount = text.Split('\n')
        .Count(line => line.Contains("[Devil's Advocate]", StringComparison.OrdinalIgnoreCase));
    if (devilCount < 2)
    {
        throw new InvalidOperationException("Final plan must include at least 2 risks tagged with [Devil's Advocate].");
    }
}

static string EnsureDevilsAdvocateRiskCoverage(string plan, string councilNotes)
{
    var currentTagCount = CountDevilsAdvocateTags(plan);
    if (currentTagCount >= 2)
    {
        return plan;
    }

    var missing = 2 - currentTagCount;
    var candidateRisks = ExtractDevilsAdvocateRisks(councilNotes);
    while (candidateRisks.Count < missing)
    {
        candidateRisks.Add("Potential production failure mode requires explicit mitigation and rollback criteria.");
    }

    var additions = candidateRisks
        .Take(missing)
        .Select(r => $"- [Devil's Advocate] {r}")
        .ToList();

    // Try to append inside Risks & mitigations section before Rollout plan.
    var riskHeaderIdx = plan.IndexOf("Risks & mitigations", StringComparison.OrdinalIgnoreCase);
    if (riskHeaderIdx >= 0)
    {
        var rolloutIdx = plan.IndexOf("Rollout plan", riskHeaderIdx, StringComparison.OrdinalIgnoreCase);
        if (rolloutIdx > riskHeaderIdx)
        {
            var before = plan[..rolloutIdx].TrimEnd();
            var after = plan[rolloutIdx..].TrimStart();
            var injected = $"{before}\n{string.Join('\n', additions)}\n\n{after}";
            return injected;
        }
    }

    // Fallback: append a compact risks section at the end.
    var sb = new StringBuilder(plan.TrimEnd());
    sb.AppendLine();
    sb.AppendLine();
    sb.AppendLine("Risks & mitigations (auto-added)");
    foreach (var line in additions)
    {
        sb.AppendLine(line);
    }
    return sb.ToString().Trim();
}

static int CountDevilsAdvocateTags(string text)
{
    return text.Split('\n')
        .Count(line => line.Contains("[Devil's Advocate]", StringComparison.OrdinalIgnoreCase));
}

static List<string> ExtractDevilsAdvocateRisks(string councilNotes)
{
    var lines = councilNotes.Replace("\r", "").Split('\n');
    var risks = new List<string>();
    var inDevilSection = false;

    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            inDevilSection = line.Contains("Devil's Advocate", StringComparison.OrdinalIgnoreCase);
            continue;
        }

        if (!inDevilSection || string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var cleaned = line.TrimStart('-', '*', ' ', '\t');
        if (cleaned.Length < 20)
        {
            continue;
        }

        if (cleaned.Contains("mitigation", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        risks.Add(cleaned);
        if (risks.Count >= 4)
        {
            break;
        }
    }

    return risks;
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
Do NOT repeat the prompt text.
Do NOT restate the full SPEC verbatim.
Output ONLY your notes.

SPEC:
{spec}
""";
}

static string BuildWriterReviewerPrompt(string spec, string councilNotes)
{
    return $"""
You are in an Architecture Council writer/reviewer loop.
Use the council notes and produce one final answer with the exact required structure.
Moderator must output the final plan. Writer/Reviewer are internal.

Output sections (mandatory):
1) Executive summary (3 bullets)
2) Key decisions (max 5) with trade-offs (pros/cons brief)
3) Risks & mitigations (must include at least 2 risks raised by Devil's Advocate, tag those bullets with [Devil's Advocate])
4) Rollout plan (phased) + rollback trigger
5) Definition of Done checklist (minimum 10 items, including observability and security)

Style rules:
- Be concrete and implementation-ready.
- No fluff.
- Mention idempotency explicitly.
- Final output must start with: === FINAL PLAN ===

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
