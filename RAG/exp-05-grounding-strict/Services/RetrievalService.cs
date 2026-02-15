using Exp05GroundingStrict.Models;
using Exp05GroundingStrict.Ollama;
using Exp05GroundingStrict.Qdrant;

namespace Exp05GroundingStrict.Services;

public sealed class RetrievalService
{
    private readonly AppConfig _config;
    private readonly OllamaEmbeddingClient _embeddings;
    private readonly QdrantClient _qdrant;

    public RetrievalService(AppConfig config, OllamaEmbeddingClient embeddings, QdrantClient qdrant)
    {
        _config = config;
        _embeddings = embeddings;
        _qdrant = qdrant;
    }

    public async Task<List<RetrievedChunk>> RetrieveAsync(
        string question,
        int topK,
        CancellationToken cancellationToken)
    {
        var count = await _qdrant.CountPointsAsync(_config.QdrantCollection, cancellationToken);
        if (count == 0)
        {
            return [];
        }

        var queryVector = await _embeddings.GetEmbeddingAsync(_config.OllamaEmbedModel, question, cancellationToken);
        return await _qdrant.SearchChunksAsync(_config.QdrantCollection, queryVector, topK, cancellationToken);
    }
}
