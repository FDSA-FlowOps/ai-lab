namespace Exp04RagBasicQdrant.Models;

public sealed record DocumentData(string DocId, string Title, string Text, string SourcePath);

public sealed class DocumentChunk
{
    public string DocId { get; init; } = string.Empty;
    public string DocTitle { get; init; } = string.Empty;
    public string SectionTitle { get; init; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int StartChar { get; init; }
    public int EndChar { get; init; }
    public string ChunkText { get; init; } = string.Empty;
    public string Strategy { get; init; } = "MarkdownAware";
    public int ChunkSize { get; init; }
    public int Overlap { get; init; }
}

public sealed class RetrievedChunk
{
    public double Score { get; init; }
    public string DocId { get; init; } = string.Empty;
    public string DocTitle { get; init; } = string.Empty;
    public string SectionTitle { get; init; } = string.Empty;
    public string ChunkId { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public string ChunkText { get; init; } = string.Empty;
}

public sealed class RuntimeSettings
{
    public int TopK { get; set; }
    public double MinRetrievalScore { get; set; }
    public bool ShowDebug { get; set; }
    public int ChunkSizeChars { get; set; }
    public int ChunkOverlapChars { get; set; }
    public int MinChunkChars { get; set; }
}

public sealed class IngestStats
{
    public int Documents { get; init; }
    public int Chunks { get; init; }
    public int MinChars { get; init; }
    public double AvgChars { get; init; }
    public int MaxChars { get; init; }
}

public sealed class RagAnswerResult
{
    public bool UsedLlm { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<RetrievedChunk> Retrieved { get; init; } = [];
    public List<string> Citations { get; init; } = [];
    public double Top1Score { get; init; }
}
