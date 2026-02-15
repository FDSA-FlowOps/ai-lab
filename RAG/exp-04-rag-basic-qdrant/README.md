# exp-04-rag-basic-qdrant

Experimento independiente de RAG basico con Qdrant + Ollama:
- Ingesta docs markdown -> chunking -> embeddings -> Qdrant.
- Preguntas RAG con guardrails anti-alucinacion y citas.

## Requisitos

- .NET 10 SDK
- Docker (Qdrant)
- Ollama local

## Config

`appsettings.json` + variables de entorno (`.env.example`):

- `OLLAMA_BASE_URL=http://localhost:11434`
- `OLLAMA_EMBED_MODEL=bge-m3`
- `OLLAMA_CHAT_MODEL=llama3.1:8b`
- `QDRANT_BASE_URL=http://localhost:6333`
- `QDRANT_COLLECTION=exp04_rag`
- `TOP_K=6`
- `MIN_RETRIEVAL_SCORE=0.60`
- `CHUNK_SIZE_CHARS=900`
- `CHUNK_OVERLAP_CHARS=140`
- `MIN_CHUNK_CHARS=220`
- `HTTP_TIMEOUT_SECONDS=60`
- `SHOW_DEBUG=true`

## Levantar servicios

Desde este folder:

```bash
docker compose up -d
ollama pull bge-m3
ollama pull llama3.1:8b
```

Verificacion rapida:

```bash
curl http://localhost:6333/collections
curl http://localhost:11434/api/tags
```

PowerShell:

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

1. Reset collection
2. Ingest documents
3. Ask a question (RAG)
4. Show retrieved chunks
5. Change runtime settings
6. Exit

## Flujo recomendado

1. Opcion 1 (reset)
2. Opcion 2 (ingest)
3. Opcion 3 (ask), ejemplo: `quiero cancelar una suscripcion y evitar renovacion`

## Guardrail anti-alucinacion

Si `top1_score < MIN_RETRIEVAL_SCORE`, no se llama al LLM y el sistema responde:
`No tengo evidencia suficiente en los documentos indexados.`

## Ejemplo de salida (ask)

```text
top1_score=0.7123 (min requerido=0.60)

Respuesta:
Para evitar la renovacion, debes cancelar antes de la fecha de corte y la baja aplica al siguiente ciclo [POLITICA-SUSCRIPCIONES|Cancelacion|6].

Citas usadas: [POLITICA-SUSCRIPCIONES|Cancelacion|6]
```

## Notas

- Si el modelo de chat no existe, el error sugiere `ollama pull <model>`.
- No incluye reranking, busqueda hibrida, evaluacion ni agentes.
