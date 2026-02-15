namespace Exp03ChunkingQdrant.Chunking;

public static class Chunker
{
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
        var headers = ExtractHeadersOutsideFences(doc.Text);
        if (headers.Count == 0)
        {
            return ChunkFixed(doc, settings, sectionTitle: "(Documento)");
        }

        var output = new List<DocumentChunk>();
        if (headers[0].Index > 0)
        {
            var preambleText = doc.Text[..headers[0].Index];
            if (!string.IsNullOrWhiteSpace(preambleText))
            {
                output.AddRange(ChunkOrSection(
                    doc,
                    settings,
                    preambleText,
                    "(Preambulo)",
                    0));
            }
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index;
            var end = i < headers.Count - 1 ? headers[i + 1].Index : doc.Text.Length;
            if (end <= start)
            {
                continue;
            }

            var sectionText = doc.Text[start..end];
            if (string.IsNullOrWhiteSpace(sectionText))
            {
                continue;
            }

            output.AddRange(ChunkOrSection(
                doc,
                settings,
                sectionText,
                headers[i].Title,
                start));
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
            var remaining = text.Length - pos;
            if (remaining < settings.MinChunkChars && pos > 0)
            {
                break;
            }

            var preferredEnd = Math.Min(text.Length, pos + effectiveSize);
            var end = FindSmartCut(text, pos, preferredEnd, settings.MinChunkChars);
            if (end <= pos)
            {
                end = preferredEnd;
            }

            var raw = text[pos..end];
            if (raw.Trim().Length >= settings.MinChunkChars || chunks.Count == 0)
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

            var next = end - overlap;
            pos = next > pos ? next : pos + step;
        }

        return Reindex(chunks);
    }

    private static List<DocumentChunk> ChunkOrSection(
        DocumentData doc,
        ChunkingSettings settings,
        string sectionText,
        string sectionTitle,
        int startOffset)
    {
        var trimmedLength = sectionText.Trim().Length;
        if (trimmedLength == 0)
        {
            return [];
        }

        if (sectionText.Length <= settings.MaxChunkChars && trimmedLength >= settings.MinChunkChars)
        {
            return
            [
                new DocumentChunk
                {
                    DocId = doc.DocId,
                    DocTitle = doc.Title,
                    SectionTitle = sectionTitle,
                    ChunkText = sectionText,
                    StartChar = startOffset,
                    EndChar = startOffset + sectionText.Length,
                    Strategy = settings.Strategy,
                    ChunkSize = settings.ChunkSizeChars,
                    Overlap = settings.ChunkOverlapChars
                }
            ];
        }

        var sectionDoc = new DocumentData(doc.DocId, doc.Title, sectionText, doc.SourcePath);
        return ChunkFixed(sectionDoc, settings, sectionTitle, startOffset);
    }

    private static int FindSmartCut(string text, int start, int preferredEnd, int minChunkChars)
    {
        if (preferredEnd - start <= minChunkChars)
        {
            return preferredEnd;
        }

        var backWindow = 120;
        var searchStart = Math.Max(start + minChunkChars, preferredEnd - backWindow);
        if (searchStart >= preferredEnd)
        {
            return preferredEnd;
        }

        var segment = text[searchStart..preferredEnd];

        var paragraphBreak = segment.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraphBreak >= 0)
        {
            return searchStart + paragraphBreak + 2;
        }

        var punct = segment.LastIndexOfAny(['.', '?', '!']);
        if (punct >= 0)
        {
            return searchStart + punct + 1;
        }

        var lineBreak = segment.LastIndexOf('\n');
        if (lineBreak >= 0)
        {
            return searchStart + lineBreak + 1;
        }

        return preferredEnd;
    }

    private static List<(int Index, string Title)> ExtractHeadersOutsideFences(string markdown)
    {
        var headers = new List<(int Index, string Title)>();
        var inFence = false;
        var i = 0;
        while (i < markdown.Length)
        {
            var lineStart = i;
            var lineEnd = markdown.IndexOf('\n', i);
            var hasNewLine = lineEnd >= 0;
            if (!hasNewLine)
            {
                lineEnd = markdown.Length;
            }

            var line = markdown[lineStart..lineEnd];
            var trimmed = line.TrimStart();

            if (IsFence(trimmed))
            {
                inFence = !inFence;
            }
            else if (!inFence && TryParseHeader(trimmed, out var title))
            {
                headers.Add((lineStart, title));
            }

            i = hasNewLine ? lineEnd + 1 : markdown.Length;
        }

        return headers;
    }

    private static bool IsFence(string line)
    {
        return line.StartsWith("```", StringComparison.Ordinal) ||
               line.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool TryParseHeader(string line, out string title)
    {
        title = string.Empty;
        if (line.Length < 3 || line[0] != '#')
        {
            return false;
        }

        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is < 1 or > 3)
        {
            return false;
        }

        if (level >= line.Length || line[level] != ' ')
        {
            return false;
        }

        title = line[(level + 1)..].Trim();
        return title.Length > 0;
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
