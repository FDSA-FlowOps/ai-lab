using System.Text;
using System.Text.RegularExpressions;
using Exp04RagBasicQdrant.Models;
using Exp04RagBasicQdrant.Ollama;

namespace Exp04RagBasicQdrant.Services;

public sealed class RagAnswerService
{
    private static readonly Regex CitationRegex = new(@"\[[^\[\]\r\n]+\|[^\[\]\r\n]+\|\d+\]", RegexOptions.Compiled);

    private readonly AppConfig _config;
    private readonly RetrievalService _retrieval;
    private readonly OllamaChatClient _chat;

    public RagAnswerService(AppConfig config, RetrievalService retrieval, OllamaChatClient chat)
    {
        _config = config;
        _retrieval = retrieval;
        _chat = chat;
    }

    public async Task<RagAnswerResult> AskAsync(
        string question,
        RuntimeSettings runtime,
        CancellationToken cancellationToken)
    {
        var retrieved = await _retrieval.RetrieveAsync(question, runtime.TopK, cancellationToken);
        if (retrieved.Count == 0)
        {
            return new RagAnswerResult
            {
                UsedLlm = false,
                Answer = "No tengo evidencia suficiente en los documentos indexados.",
                Retrieved = retrieved,
                Top1Score = 0
            };
        }

        var top1 = retrieved[0].Score;
        if (top1 < runtime.MinRetrievalScore)
        {
            return new RagAnswerResult
            {
                UsedLlm = false,
                Answer = "No tengo evidencia suficiente en los documentos indexados.",
                Retrieved = retrieved,
                Top1Score = top1
            };
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(question, retrieved);
        var answer = await _chat.ChatAsync(_config.OllamaChatModel, systemPrompt, userPrompt, cancellationToken);
        var citations = ExtractCitations(answer);

        return new RagAnswerResult
        {
            UsedLlm = true,
            Answer = answer,
            Retrieved = retrieved,
            Citations = citations,
            Top1Score = top1
        };
    }

    private static string BuildSystemPrompt()
    {
        return """
Eres un asistente de soporte que responde SOLO con la evidencia del contexto provisto.
Reglas obligatorias:
1) No inventes datos, politicas, numeros ni fuentes.
2) Si falta evidencia suficiente, dilo explicitamente.
3) Cita siempre usando formato exacto [doc_id|section_title|chunk_index].
4) Usa un tono claro y breve.
""";
    }

    private static string BuildUserPrompt(string question, IReadOnlyList<RetrievedChunk> retrieved)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Contexto recuperado:");
        sb.AppendLine();
        for (var i = 0; i < retrieved.Count; i++)
        {
            var r = retrieved[i];
            sb.AppendLine($"[{r.DocId}|{r.SectionTitle}|{r.ChunkIndex}] score={r.Score:0.0000}");
            sb.AppendLine(r.ChunkText.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Pregunta del usuario:");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("Responde usando solo el contexto anterior y cita siempre.");
        return sb.ToString();
    }

    private static List<string> ExtractCitations(string answer)
    {
        var matches = CitationRegex.Matches(answer);
        return matches
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
