# exp-02-vectordb-qdrant

Experimento independiente para ingestar documentos de soporte en Qdrant usando embeddings de Ollama (`bge-m3`) y consultar similaridad vectorial.

Notas de implementacion:
- Endpoint moderno de Ollama: `POST /api/embed`.
- `ingest` usa embeddings batch por chunks para acelerar carga de documentos.

## Requisitos

- .NET 10 SDK
- Docker Desktop (o Docker Engine) para levantar Qdrant
- Ollama local en `http://localhost:11434`

## Estructura

- `Program.cs`: CLI (`ingest`, `query`, `reset`)
- `Ollama/OllamaEmbeddingClient.cs`: cliente HTTP embeddings
- `Qdrant/QdrantClient.cs`: cliente HTTP de coleccion, upsert y search
- `Data/Samples/docs.jsonl`: dataset demo (40 docs)
- `data/docs.jsonl`: copia inicial para ingest desde `./data`
- `docker-compose.yml`: servicio Qdrant local

## Configuracion

Valores por defecto (`appsettings.json`):

- `OllamaBaseUrl`: `http://localhost:11434`
- `OllamaModel`: `bge-m3`
- `QdrantBaseUrl`: `http://localhost:6333`
- `QdrantCollection`: `exp02_docs`
- `TopK`: `5`
- `HttpTimeoutSeconds`: `30`

Variables de entorno (`.env.example`):

- `OLLAMA_BASE_URL`
- `OLLAMA_MODEL`
- `QDRANT_BASE_URL`
- `QDRANT_COLLECTION`
- `TOP_K`
- `HTTP_TIMEOUT_SECONDS`

Ejemplo PowerShell:

```powershell
$env:OLLAMA_BASE_URL="http://localhost:11434"
$env:OLLAMA_MODEL="bge-m3"
$env:QDRANT_BASE_URL="http://localhost:6333"
$env:QDRANT_COLLECTION="exp02_docs"
$env:TOP_K="5"
$env:HTTP_TIMEOUT_SECONDS="30"
```

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

## Comandos CLI

### 0) Modo interactivo (recomendado para pruebas)

Inicia sin argumentos:

```bash
dotnet run
```

Comandos en el prompt:

```text
ingest
query <texto>
reset
help
exit
```

### 1) reset

Borra y recrea la coleccion con distancia `Cosine` y `vector size` dinamico (tomado del primer embedding del dataset).

```bash
dotnet run -- reset
```

### 2) ingest

Lee `.jsonl` desde `./data`, genera embeddings y hace upsert en Qdrant con payload:

- `doc_id`
- `title`
- `source`
- `text`
- `snippet`
- `tags`

```bash
dotnet run -- ingest
```

### 3) query

Genera embedding de query y busca `TopK` en Qdrant.

```bash
dotnet run -- query "quiero cancelar mi plan antes de la renovacion"
```

Ejemplo de salida:

```text
Top 5 resultados para: "quiero cancelar mi plan antes de la renovacion"
1. score=0.9012 | doc_id=SUP-007 | title=Cancelar suscripcion antes de renovar
   snippet: Pasos para dar de baja el plan mensual antes de la fecha de renovacion automatica.
2. score=0.8799 | doc_id=SUP-009 | title=Desactivar renovacion automatica
   snippet: Instrucciones para apagar auto-renew sin perder acceso hasta fin de ciclo.
```

## Flujo recomendado (copy/paste)

```bash
docker compose up -d
ollama pull bge-m3
dotnet restore
dotnet build
dotnet run -- reset
dotnet run -- ingest
dotnet run -- query "no puedo iniciar sesion con google"
dotnet run -- query "me cobraron dos veces"
dotnet run -- query "quiero cancelar la suscripcion"
```

## Errores comunes

- Qdrant caido:
  - mensaje esperado: error de conexion a `http://localhost:6333`
  - accion: `docker compose up -d`
- Ollama caido o modelo faltante:
  - mensaje esperado: error de conexion a `http://localhost:11434` o modelo no encontrado
  - accion: levantar Ollama y ejecutar `ollama pull bge-m3`

## Alcance del experimento

- Solo embeddings + vector DB (Qdrant).
- No incluye generacion de respuesta con LLM ni pipeline RAG completo.
