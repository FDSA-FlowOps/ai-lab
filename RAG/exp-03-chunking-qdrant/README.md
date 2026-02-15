# exp-03-chunking-qdrant

Experimento independiente para evaluar chunking de documentos largos, ingestar chunks en Qdrant y probar retrieval desde un menu interactivo.

## Requisitos

- .NET 10 SDK
- Docker (para Qdrant)
- Ollama local en `http://localhost:11434`

## Configuracion

Valores por defecto en `appsettings.json`:

- `OllamaBaseUrl`: `http://localhost:11434`
- `OllamaModel`: `bge-m3`
- `QdrantBaseUrl`: `http://localhost:6333`
- `QdrantCollection`: `exp03_chunks`
- `TopK`: `5`
- `ChunkStrategy`: `MarkdownAware`
- `ChunkSizeChars`: `800`
- `ChunkOverlapChars`: `120`
- `MinChunkChars`: `200`
- `MaxChunkChars`: `1200`
- `HttpTimeoutSeconds`: `30`

Variables de entorno disponibles en `.env.example`.

## Dataset

`./data` incluye 4 documentos markdown largos en espanol:

- `politica-reembolsos.md`
- `faq-autenticacion.md`
- `guia-webhooks.md`
- `manual-oncall.md`

Todos incluyen headers, listas y parrafos para comparar estrategias de chunking.

## Levantar servicios

Desde este folder:

```bash
docker compose up -d
ollama pull bge-m3
```

Validacion rapida:

```bash
curl http://localhost:6333/collections
curl http://localhost:11434/api/tags
```

En PowerShell puedes usar:

```powershell
curl.exe http://localhost:6333/collections
curl.exe http://localhost:11434/api/tags
```

## Ejecutar

```bash
dotnet restore
dotnet build
dotnet run
```

## Menu interactivo

Opciones:

1. `Reset collection`
2. `Ingest` (muestra stats docs/chunks/min-avg-max chars)
3. `Query`
4. `Show chunking preview`
5. `Change settings` (runtime)
6. `Exit`

## Flujo sugerido para comparar chunking

1. `4` -> preview con estrategia actual.
2. `1` -> reset collection.
3. `2` -> ingest.
4. `3` -> query (ejemplo: `quiero cancelar una suscripcion y evitar renovacion`).
5. `5` -> cambia a `FixedSizeWithOverlap` y ajusta size/overlap.
6. Repite `1`, `2`, `3` y compara resultados.

## Estrategias implementadas

- `FixedSizeWithOverlap`
  - chunks por ventanas de caracteres.
  - usa `ChunkSizeChars` y `ChunkOverlapChars`.
- `MarkdownAware`
  - primero divide por headers (`#`, `##`, `###`).
  - si una seccion supera limite, subdivide con `FixedSizeWithOverlap`.

## Payload en Qdrant

Cada punto (chunk) incluye:

- `doc_id`, `doc_title`
- `chunk_id`, `chunk_index`
- `chunk_text`
- `section_title`
- `start_char`, `end_char`
- `strategy`, `chunk_size`, `overlap`

## Alcance

- Solo chunking + embeddings + retrieval vectorial.
- No incluye generacion con LLM ni pipeline RAG completo.
