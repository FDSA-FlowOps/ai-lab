using System.Text.RegularExpressions;

namespace Exp03ChunkingQdrant.Chunking;

public static class Chunker
{
    private static readonly Regex HeaderRegex = new(
        @"^(#{1,3})\s+(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static List<DocumentChunk> BuildChunks(
        DocumentData doc,
        ChunkingSettings settings)
    {
        return settings.Strategy switch
        {
            ChunkStrategy.FixedSizeWithOverlap => ChunkFixed(doc, settings),
            ChunkStrategy.MarkdownAware => ChunkMarkdownAware(doc, settings),
            _ => throw new InvalidOperationException($"Estrategia no soportada: {settings.Strategy}")
        };
    }

    private static List<DocumentChunk> ChunkMarkdownAware(DocumentData doc, ChunkingSettings settings)
    {
        var matches = HeaderRegex.Matches(doc.Text);
        if (matches.Count == 0)
        {
            return ChunkFixed(doc, settings, sectionTitle: "(Documento)");
        }

        var output = new List<DocumentChunk>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index;
            var end = i < matches.Count - 1 ? matches[i + 1].Index : doc.Text.Length;
            if (end <= start)
            {
                continue;
            }

            var sectionText = doc.Text[start..end].Trim();
            if (sectionText.Length == 0)
            {
                continue;
            }

            var sectionTitle = match.Groups[2].Value.Trim();
            if (sectionText.Length <= settings.MaxChunkChars && sectionText.Length >= settings.MinChunkChars)
            {
                output.Add(new DocumentChunk
                {
                    DocId = doc.DocId,
                    DocTitle = doc.Title,
                    SectionTitle = sectionTitle,
                    ChunkText = sectionText,
                    StartChar = start,
                    EndChar = end,
                    Strategy = settings.Strategy,
                    ChunkSize = settings.ChunkSizeChars,
                    Overlap = settings.ChunkOverlapChars
                });
                continue;
            }

            var sectionDoc = new DocumentData(doc.DocId, doc.Title, sectionText, doc.SourcePath);
            var split = ChunkFixed(sectionDoc, settings, sectionTitle, start);
            output.AddRange(split);
        }

        return Reindex(output);
    }

    private static List<DocumentChunk> ChunkFixed(
        DocumentData doc,
        ChunkingSettings settings,
        string sectionTitle = "(Sin seccion)",
        int baseOffset = 0)
    {
        var text = doc.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var effectiveSize = Math.Min(settings.ChunkSizeChars, settings.MaxChunkChars);
        if (effectiveSize < settings.MinChunkChars)
        {
            effectiveSize = settings.MinChunkChars;
        }

        var overlap = Math.Min(settings.ChunkOverlapChars, effectiveSize - 1);
        var step = Math.Max(1, effectiveSize - overlap);

        var chunks = new List<DocumentChunk>();
        var pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(text.Length, pos + effectiveSize);
            var len = end - pos;

            if (len < settings.MinChunkChars && pos > 0)
            {
                break;
            }

            var raw = text[pos..end].Trim();
            if (raw.Length >= settings.MinChunkChars || (pos == 0 && raw.Length > 0))
            {
                chunks.Add(new DocumentChunk
                {
                    DocId = doc.DocId,
                    DocTitle = doc.Title,
                    SectionTitle = sectionTitle,
                    ChunkText = raw,
                    StartChar = baseOffset + pos,
                    EndChar = baseOffset + end,
                    Strategy = settings.Strategy,
                    ChunkSize = settings.ChunkSizeChars,
                    Overlap = settings.ChunkOverlapChars
                });
            }

            if (end >= text.Length)
            {
                break;
            }

            pos += step;
        }

        return Reindex(chunks);
    }

    private static List<DocumentChunk> Reindex(List<DocumentChunk> chunks)
    {
        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkIndex = i;
            chunks[i].ChunkId = $"{chunks[i].DocId}_chunk_{i:D4}";
        }

        return chunks;
    }
}

public sealed record DocumentData(string DocId, string Title, string Text, string SourcePath);

public enum ChunkStrategy
{
    FixedSizeWithOverlap,
    MarkdownAware
}

public sealed class ChunkingSettings
{
    public ChunkStrategy Strategy { get; set; } = ChunkStrategy.MarkdownAware;
    public int ChunkSizeChars { get; set; } = 800;
    public int ChunkOverlapChars { get; set; } = 120;
    public int MinChunkChars { get; set; } = 200;
    public int MaxChunkChars { get; set; } = 1200;
}

public sealed class DocumentChunk
{
    public string DocId { get; init; } = string.Empty;
    public string DocTitle { get; init; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string ChunkText { get; init; } = string.Empty;
    public string SectionTitle { get; init; } = string.Empty;
    public int StartChar { get; init; }
    public int EndChar { get; init; }
    public ChunkStrategy Strategy { get; init; }
    public int ChunkSize { get; init; }
    public int Overlap { get; init; }
}
