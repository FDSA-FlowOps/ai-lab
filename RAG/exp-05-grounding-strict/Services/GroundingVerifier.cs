using System.Text.RegularExpressions;
using Exp05GroundingStrict.Models;

namespace Exp05GroundingStrict.Services;

public sealed class GroundingVerifier
{
    private static readonly Regex CitationRegex = new(@"\[D\d+\|[^\|\]]+\|c\d+\]", RegexOptions.Compiled);

    public GroundingVerificationResult Verify(string answer, IReadOnlyList<RetrievedChunk> retrieved)
    {
        var lines = answer
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return GroundingVerificationResult.Invalid("Sin contenido.");
        }

        var allowed = retrieved
            .Select(r => BuildCitation(r.DocId, r.SectionTitle, r.ChunkIndex))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var citations = new List<string>();
        foreach (var line in lines)
        {
            if (!(line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)))
            {
                return GroundingVerificationResult.Invalid("La salida contiene lineas que no son bullets.");
            }

            var lineCitations = CitationRegex.Matches(line).Select(m => m.Value).ToList();
            if (lineCitations.Count == 0)
            {
                return GroundingVerificationResult.Invalid("Hay bullets sin citas.");
            }

            foreach (var c in lineCitations)
            {
                if (!allowed.Contains(c))
                {
                    return GroundingVerificationResult.Invalid($"Cita no valida: {c}");
                }
            }

            citations.AddRange(lineCitations);
        }

        return GroundingVerificationResult.Valid(citations.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static string BuildCitation(string docId, string sectionTitle, int chunkIndex)
    {
        return $"[D{docId}|{sectionTitle}|c{chunkIndex}]";
    }
}

public sealed class GroundingVerificationResult
{
    public bool IsValid { get; init; }
    public string? Reason { get; init; }
    public List<string> Citations { get; init; } = [];

    public static GroundingVerificationResult Invalid(string reason) => new()
    {
        IsValid = false,
        Reason = reason
    };

    public static GroundingVerificationResult Valid(List<string> citations) => new()
    {
        IsValid = true,
        Citations = citations
    };
}
