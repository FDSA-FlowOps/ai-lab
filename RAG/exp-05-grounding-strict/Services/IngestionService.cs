using Exp05GroundingStrict.Chunking;
using Exp05GroundingStrict.Models;
using Exp05GroundingStrict.Ollama;
using Exp05GroundingStrict.Qdrant;

namespace Exp05GroundingStrict.Services;

public sealed class IngestionService
{
    private const int EmbedBatchSize = 24;

    private readonly string _root;
    private readonly AppConfig _config;
    private readonly OllamaEmbeddingClient _embeddings;
    private readonly QdrantClient _qdrant;

    public IngestionService(string root, AppConfig config, OllamaEmbeddingClient embeddings, QdrantClient qdrant)
    {
        _root = root;
        _config = config;
        _embeddings = embeddings;
        _qdrant = qdrant;
    }

    public async Task ResetCollectionAsync(RuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var chunks = LoadAndChunk(runtime);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No hay chunks para detectar dimension.");
        }

        Console.WriteLine("[INFO] Detectando dimension de embedding...");
        var probe = await _embeddings.GetEmbeddingAsync(_config.OllamaEmbedModel, chunks[0].ChunkText, cancellationToken);

        await _qdrant.DeleteCollectionIfExistsAsync(_config.QdrantCollection, cancellationToken);
        await _qdrant.CreateCollectionAsync(_config.QdrantCollection, probe.Length, cancellationToken);
        Console.WriteLine($"[OK] Coleccion '{_config.QdrantCollection}' recreada (vector size={probe.Length}).");
    }

    public async Task<IngestStats> IngestAsync(RuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var docs = DocumentLoader.Load(_root);
        var chunks = BuildChunks(docs, runtime);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No se generaron chunks para ingest.");
        }

        Console.WriteLine($"[INFO] Ingestando {docs.Count} docs y {chunks.Count} chunks...");
        var firstBatchCount = Math.Min(EmbedBatchSize, chunks.Count);
        var firstTexts = chunks.Take(firstBatchCount).Select(c => c.ChunkText).ToArray();
        var firstVectors = await _embeddings.GetEmbeddingsAsync(_config.OllamaEmbedModel, firstTexts, cancellationToken);
        if (firstVectors.Length != firstBatchCount)
        {
            throw new InvalidOperationException("Cantidad inesperada de embeddings en primer batch.");
        }

        // Recreate collection on each ingest to avoid stale points when chunk count shrinks.
        await _qdrant.DeleteCollectionIfExistsAsync(_config.QdrantCollection, cancellationToken);
        await _qdrant.CreateCollectionAsync(_config.QdrantCollection, firstVectors[0].Length, cancellationToken);

        var points = new List<QdrantPoint>(chunks.Count);
        for (var i = 0; i < firstBatchCount; i++)
        {
            points.Add(ToPoint(i, chunks[i], firstVectors[i]));
        }

        for (var start = firstBatchCount; start < chunks.Count; start += EmbedBatchSize)
        {
            var count = Math.Min(EmbedBatchSize, chunks.Count - start);
            Console.WriteLine($"[INFO] Embedding chunks {start + 1}-{start + count}/{chunks.Count}...");
            var texts = chunks.Skip(start).Take(count).Select(c => c.ChunkText).ToArray();
            var vectors = await _embeddings.GetEmbeddingsAsync(_config.OllamaEmbedModel, texts, cancellationToken);
            if (vectors.Length != count)
            {
                throw new InvalidOperationException("Cantidad inesperada de embeddings en batch.");
            }

            for (var i = 0; i < count; i++)
            {
                points.Add(ToPoint(start + i, chunks[start + i], vectors[i]));
            }
        }

        await _qdrant.UpsertPointsAsync(_config.QdrantCollection, points, cancellationToken);

        var lengths = chunks.Select(c => c.ChunkText.Length).ToList();
        return new IngestStats
        {
            Documents = docs.Count,
            Chunks = chunks.Count,
            MinChars = lengths.Min(),
            AvgChars = lengths.Average(),
            MaxChars = lengths.Max()
        };
    }

    private List<DocumentChunk> LoadAndChunk(RuntimeSettings runtime)
    {
        var docs = DocumentLoader.Load(_root);
        return BuildChunks(docs, runtime);
    }

    private static List<DocumentChunk> BuildChunks(IReadOnlyList<DocumentData> docs, RuntimeSettings runtime)
    {
        ValidateChunking(runtime);
        var all = new List<DocumentChunk>();
        foreach (var doc in docs)
        {
            all.AddRange(Chunker.BuildMarkdownAwareChunks(doc, runtime));
        }

        return all;
    }

    private static void ValidateChunking(RuntimeSettings runtime)
    {
        if (runtime.ChunkSizeChars <= 0 || runtime.ChunkOverlapChars < 0 || runtime.MinChunkChars <= 0)
        {
            throw new InvalidOperationException("Chunk settings invalidos.");
        }

        if (runtime.ChunkOverlapChars >= runtime.ChunkSizeChars)
        {
            throw new InvalidOperationException("Chunk overlap debe ser menor que chunk size.");
        }
    }

    private static QdrantPoint ToPoint(int index, DocumentChunk chunk, float[] vector)
    {
        return new QdrantPoint
        {
            Id = index + 1,
            Vector = vector,
            Payload = new Dictionary<string, object?>
            {
                ["doc_id"] = chunk.DocId,
                ["doc_title"] = chunk.DocTitle,
                ["section_title"] = chunk.SectionTitle,
                ["chunk_id"] = chunk.ChunkId,
                ["chunk_index"] = chunk.ChunkIndex,
                ["start_char"] = chunk.StartChar,
                ["end_char"] = chunk.EndChar,
                ["chunk_text"] = chunk.ChunkText,
                ["strategy"] = chunk.Strategy,
                ["chunk_size"] = chunk.ChunkSize,
                ["overlap"] = chunk.Overlap
            }
        };
    }
}
