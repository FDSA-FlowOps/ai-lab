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
    using var chatClient = BuildChatClient(ollamaBaseUrl, ollamaModel);

    Console.WriteLine($"[INFO] Using Ollama model '{ollamaModel}' at {ollamaBaseUrl}");
    Console.WriteLine("[INFO] Running Architecture Council concurrent analysis...");

    var councilNotes = await RunCouncilConcurrentAsync(chatClient, spec, ollamaModel);

    Console.WriteLine("[INFO] Running Writer/Reviewer group chat (4 iterations)...");

    var finalPlan = await RunWriterReviewerAsync(chatClient, spec, councilNotes, ollamaModel);

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

static async Task<string> RunCouncilConcurrentAsync(IChatClient client, string spec, string model)
{
    var architect = MakeAgent(client,
        "Architect",
        "You are the system architect. Focus on architecture shape, boundaries, dependencies, data model, and trade-offs. Keep it concrete.",
        "System architecture lead");

    var security = MakeAgent(client,
        "Security",
        "You are the security engineer. Focus on authn/authz, data protection, secrets, abuse prevention, and compliance implications.",
        "Security specialist");

    var sre = MakeAgent(client,
        "SRE",
        "You are the SRE. Focus on reliability, SLOs, rollback, failure domains, observability, and operability.",
        "Reliability engineer");

    var pm = MakeAgent(client,
        "PM",
        "You are the product manager. Focus on scope, user impact, sequencing, non-goals, and measurable outcomes.",
        "Product manager");

    var devilsAdvocate = MakeAgent(client,
        "DevilsAdvocate",
        "You are Devil's Advocate. Challenge assumptions aggressively. Output only risks, edge cases, and uncomfortable questions. Include sections: 'How this fails in production' and 'Abuse cases'. Do not propose pretty solutions without a risk justification.",
        "Critical risk challenger");

    var councilAgents = new[] { architect, security, sre, pm, devilsAdvocate };

    var workflow = AgentWorkflowBuilder.BuildConcurrent(
        "arch-council-concurrent",
        councilAgents,
        aggregator: null);

    var input = new List<AiChatMessage>
    {
        new(ChatRole.User, BuildCouncilPrompt(spec)),
    };

    var run = await InProcessExecution.RunAsync(workflow, input);
    EnsureRunSucceeded(run, model);

    var notes = CollectWorkflowText(run);
    if (string.IsNullOrWhiteSpace(notes))
    {
        throw new InvalidOperationException("Concurrent workflow did not produce notes.");
    }

    return notes;
}

static async Task<string> RunWriterReviewerAsync(IChatClient client, string spec, string councilNotes, string model)
{
    var writer = MakeAgent(client,
        "Writer",
        "You write the final plan. Obey exact output format and include concrete detail. Keep concise but complete.",
        "Plan writer");

    var reviewer = MakeAgent(client,
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

    var run = await InProcessExecution.RunAsync(workflow, input);
    EnsureRunSucceeded(run, model);
    var finalText = CollectWorkflowText(run);

    if (string.IsNullOrWhiteSpace(finalText))
    {
        throw new InvalidOperationException("Group chat workflow did not produce final plan.");
    }

    return finalText;
}

static string CollectWorkflowText(Run run)
{
    var sb = new StringBuilder();
    var eventTypes = new HashSet<string>(StringComparer.Ordinal);

    foreach (var evt in run.OutgoingEvents)
    {
        eventTypes.Add(evt.GetType().FullName ?? evt.GetType().Name);

        if (evt is WorkflowOutputEvent outputEvent)
        {
            if (outputEvent.Is<AgentResponse>(out var response) && response is not null)
            {
                AppendAgentResponseText(sb, response);
            }

            if (outputEvent.Is<List<AiChatMessage>>(out var messages) && messages is not null)
            {
                AppendMessagesText(sb, messages);
            }

            if (outputEvent.Is<AiChatMessage>(out var singleMessage) && singleMessage is not null)
            {
                AppendMessageText(sb, singleMessage);
            }

            if (outputEvent.Is<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text.Trim());
            }
        }
    }

    var textResult = sb.ToString().Trim();
    if (string.IsNullOrWhiteSpace(textResult))
    {
        throw new InvalidOperationException(
            $"No workflow text output found. Event types: {string.Join(", ", eventTypes)}");
    }

    return textResult;
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

static void AppendAgentResponseText(StringBuilder sb, AgentResponse response)
{
    if (!string.IsNullOrWhiteSpace(response.Text))
    {
        sb.AppendLine(response.Text.Trim());
    }

    if (response.Messages is { Count: > 0 })
    {
        AppendMessagesText(sb, response.Messages);
    }
}

static void AppendMessagesText(StringBuilder sb, IEnumerable<AiChatMessage> messages)
{
    foreach (var msg in messages)
    {
        AppendMessageText(sb, msg);
    }
}

static void AppendMessageText(StringBuilder sb, AiChatMessage message)
{
    if (!string.IsNullOrWhiteSpace(message.Text))
    {
        sb.AppendLine(message.Text.Trim());
    }
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
