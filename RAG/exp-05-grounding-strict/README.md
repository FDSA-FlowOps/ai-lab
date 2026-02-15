# exp-05-grounding-strict

Experimento independiente de RAG con grounding estricto:
- Retrieval de chunks en Qdrant.
- Respuesta del LLM en formato obligatorio de bullets con citas verificables.
- Verificador que rechaza salidas sin grounding.

## Requisitos

- .NET 10 SDK
- Docker (Qdrant)
- Ollama local

## Configuracion

`appsettings.json` y `.env.example`:

- `OLLAMA_BASE_URL=http://localhost:11434`
- `OLLAMA_EMBED_MODEL=bge-m3`
- `OLLAMA_CHAT_MODEL=llama3.1:8b`
- `QDRANT_BASE_URL=http://localhost:6333`
- `QDRANT_COLLECTION=exp05_grounding`
- `TOP_K=8`
- `MIN_RETRIEVAL_SCORE=0.60`
- `MAX_CONTEXT_CHARS_BUDGET=12000`
- `MAX_CHUNK_CHARS_FOR_PROMPT=1600`
- `MIN_RETRIEVAL_GAP=0.02`
- `CHAT_TEMPERATURE=0.2`
- `CHAT_TOP_P=0.9`
- `CHAT_NUM_CTX=8192`
- `SHOW_DEBUG=true`

## Levantar servicios

```bash
docker compose up -d
ollama pull bge-m3 && ollama pull llama3.1:8b
```

## Ejecutar

```bash
docker compose up -d ; dotnet run
```

o

```bash
dotnet run
```

## Menu interactivo

1. Reset collection
2. Ingest documents (mostrar stats)
3. Ask grounded (bullets con citas) + verificacion
4. Show retrieved chunks for a question (solo retrieval)
5. Change runtime settings (topK, minScore, context budget, chat options)
6. Exit

## Flow completo de pruebas (modo interactivo)

Dentro del menu, ejecuta este orden:

1. `1` Reset collection
2. `2` Ingest documents
3. `4` Show retrieved chunks  
Pregunta sugerida: `quiero cancelar una suscripcion y evitar renovacion`
4. `3` Ask grounded  
Pregunta sugerida: `quiero cancelar una suscripcion y evitar renovacion`
5. `3` Ask grounded (caso fuera de dominio)  
Pregunta sugerida: `quien gano el mundial de 2010`
6. `5` Change runtime settings
- Sube `MIN_RETRIEVAL_SCORE` a `0.75`
- Repite opcion `3` con la misma pregunta y compara
7. `6` Exit

Que validar:
- Opcion `2`: muestra stats de ingesta (docs/chunks/min-avg-max).
- Opcion `4`: devuelve chunks con score y cita candidata.
- Opcion `3` (caso normal): respuesta en bullets y con citas validas.
- Opcion `3` (caso sin evidencia): no debe inventar; debe cortar por guardrail o por verificador.

## Formato de salida valido (ejemplo)

```text
- Puedes cancelar antes de la renovacion desde la pantalla de Suscripcion. [D3|Cancelaciones|c2]
- Si ya se renovo, la cancelacion aplica al siguiente ciclo. [D3|Cancelaciones|c4]
```

## Ejemplo de salida invalida (rechazada)

```text
Puedes cancelar cuando quieras.
- Si no hay pago, no aplica. [D99|Inventado|c1]
```

Motivos de rechazo:
- lineas que no son bullets
- bullets sin cita
- citas no presentes en los chunks recuperados

Cuando falla verificacion:
- salida: `Respuesta inválida (sin grounding suficiente).`
- se muestran chunks recuperados para depuracion
- sugerencia: aumentar `TOP_K` o ajustar budget de contexto
