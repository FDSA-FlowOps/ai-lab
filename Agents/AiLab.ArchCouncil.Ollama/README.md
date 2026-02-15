# AiLab.ArchCouncil.Ollama

Demo multiagente en .NET 10 usando **Microsoft Agent Framework** y Ollama local (sin Azure/OpenAI cloud).

## Qué hace

Ejecuta un flujo tipo **Architecture Council**:

1. **Concurrent orchestration** con 5 agentes en paralelo:
   - Architect
   - Security
   - SRE
   - PM
   - Devil's Advocate
2. **Group chat orchestration** (Writer + Reviewer) con `RoundRobinGroupChatManager` y `MaximumIterationCount = 4`.

Salida final esperada por consola:
1. Executive summary (3 bullets)
2. Key decisions (máx 5) con trade-offs
3. Risks & mitigations (incluyendo riesgos de Devil's Advocate)
4. Rollout plan + rollback trigger
5. Definition of Done checklist (>=10 items)

## Requisitos

1. .NET 10 SDK
2. Ollama instalado y corriendo en local
3. Modelo descargado (por defecto `llama3.2`):

```bash
ollama pull llama3.2
```

## Configuración (env vars)

Opcionales. Si no se definen, usa defaults:

- `OLLAMA_BASE_URL` (default: `http://localhost:11434/v1`)
- `OLLAMA_MODEL` (default: `llama3.2`)

PowerShell:

```powershell
$env:OLLAMA_BASE_URL="http://localhost:11434/v1"
$env:OLLAMA_MODEL="llama3.2"
```

## Ejecutar

Desde la carpeta del proyecto:

```bash
dotnet run
```

Con spec por argumento:

```bash
dotnet run -- --spec "Diseñar API de suscripciones con idempotencia y rollback"
```

Con salida adicional JSON:

```bash
dotnet run -- --spec "Diseñar API de suscripciones" --json
```

## Comportamiento de errores

Si no puede conectar con Ollama:

```text
No se puede conectar a Ollama en <url>. ¿Está Ollama arrancado?
```

Si el modelo no existe en Ollama:

```text
Modelo de chat '<model>' no encontrado en Ollama. Ejecuta: ollama pull <model>
```

En ambos casos el proceso termina con `exit code != 0`.
