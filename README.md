# Actions con IA

Este repositorio contiene una propuesta base de GitHub Actions con IA para reutilizar entre distintos repositorios.

Las actions contempladas son:
- `commit_analyzer`
- `pull_request_reviewer`
- `daily_report`

La definicion funcional y las reglas comunes estan en `.github/ai-actions-standard.md`.

## Objetivo
Separar claramente tres responsabilidades:

- `commit_analyzer`
  Resume los cambios de un `push` y genera una salida reutilizable para changelog.
- `pull_request_reviewer`
  Analiza una pull request, propone o normaliza su descripcion y selecciona los agentes de revision aplicables.
- `daily_report`
  Resume la actividad del repositorio de las ultimas 24 horas.

## Estructura
- `.github/workflows/ai_commit_analyzer.yml`
- `.github/workflows/pr_reviewer.yml`
- `.github/workflows/daily_report.yml`
- `.github/prompts/commit-analyzer.md`
- `.github/prompts/pull-request-reviewer.md`
- `.github/pull_request_template.md`
- `.github/scripts/build-commit-context.sh`
- `.github/scripts/build-pr-context.sh`
- `.github/scripts/select-agents.js`
- `.github/agent-catalog/catalog.json`

## Requisitos
- Secret `AZURE_OPENAI_API_KEY`
- Endpoint de Responses API configurado en los workflows
- Permisos de lectura del repositorio
- Permisos de escritura en pull requests para `pull_request_reviewer`

## Como usar `commit_analyzer`
### Proposito
Analiza un `push` y genera:
- resumen tecnico
- clasificacion del cambio
- evaluacion del mensaje de commit
- entrada de changelog

### Disparador
- `push`
- `workflow_dispatch`

### Salidas
- `commit-analysis.md`
- `changelog-entry.md`
- `GITHUB_STEP_SUMMARY`
- artefacto `commit-analyzer`

### Configuracion
Se apoya en:
- `.github/scripts/build-commit-context.sh`
- `.github/prompts/commit-analyzer.md`

Si quieres cambiar el estilo de salida:
- modifica el prompt
- mantén estable la estructura Markdown esperada por el workflow

## Como usar `pull_request_reviewer`
### Proposito
Analiza la PR, propone una descripcion basada en plantilla y publica un resumen de revision.

### Disparador
- `pull_request` en `opened`, `synchronize`, `reopened`

### Que hace
1. Construye el contexto de la PR.
2. Lee la plantilla `.github/pull_request_template.md`.
3. Calcula agentes candidatos a partir del catalogo y las reglas.
4. Ejecuta el analisis con IA.
5. Extrae la descripcion propuesta de la PR.
6. Actualiza el cuerpo de la PR dentro de un bloque gestionado.
7. Publica el resumen en `GITHUB_STEP_SUMMARY`.

### Salidas
- `pr-review.md`
- `pr-body-generated.md`
- `selected-agents.json`
- `selected-agents.md`
- `change-metrics.json`
- artefacto `pull-request-reviewer`

### Como se actualiza la descripcion de la PR
El workflow inserta o actualiza un bloque gestionado entre estos marcadores:

```md
<!-- ai-pr-reviewer:start -->
...
<!-- ai-pr-reviewer:end -->
```

Esto permite:
- regenerar la descripcion sin destruir el resto del cuerpo
- mantener una zona controlada por automatizacion

## Como usar `daily_report`
### Proposito
Generar un informe diario de actividad de las ultimas 24 horas.

### Disparador
- `schedule`
- `workflow_dispatch`

### Salidas
- `daily-report.md`
- `GITHUB_STEP_SUMMARY`

## Configuracion de agentes
La seleccion de agentes se define en `.github/agent-catalog/catalog.json`.

### Campos habituales por agente
- `id`
- `description`
- `match`
- `priority`
- `group`
- `primary`
- `min_lines_changed`
- `min_files_changed`
- `critical_paths`
- `enabled`

### Campos habituales en `policy`
- `max_agents_per_pr`
- `max_agents_by_size`
- `tiny_max_changed_lines`
- `small_max_changed_lines`
- `medium_max_changed_lines`
- `tiny_primary_only`
- `small_primary_only`
- `critical_paths_override_limits`

## Como se aplican las reglas de agentes
La aplicacion real de reglas ocurre en `.github/scripts/select-agents.js`.

Orden de evaluacion:
1. Lee los ficheros modificados.
2. Lee las metricas del cambio desde `change-metrics.json`.
3. Clasifica el tamano de la PR: `tiny`, `small`, `medium`, `large`.
4. Obtiene agentes candidatos por `match`.
5. Fuerza agentes si coinciden `critical_paths`.
6. Descarta agentes deshabilitados.
7. Aplica `min_lines_changed` y `min_files_changed`.
8. Aplica restricciones de PR pequena, por ejemplo `tiny_primary_only`.
9. Resuelve exclusividad por `group`.
10. Ordena por `priority`.
11. Recorta por `max_agents_per_pr` y `max_agents_by_size`.

### Donde se ve el resultado
El selector genera:
- `selected-agents.json`
- `selected-agents.md`

Ambos incluyen:
- tamano estimado del cambio
- metricas del diff
- reglas aplicadas
- agentes finales
- agentes descartados y motivo

## Reglas recomendadas
- Limitar el numero de agentes por PR
- Usar prioridad para resolver exceso de candidatos
- Evitar agentes redundantes del mismo dominio
- Tratar rutas criticas como excepciones controladas
- No usar agentes especializados para cambios triviales

Las reglas completas estan en `.github/ai-actions-standard.md`.

## Adopcion en otro repositorio
1. Copiar workflows, prompts y scripts bajo `.github/`.
2. Configurar `AZURE_OPENAI_API_KEY`.
3. Ajustar el endpoint de Responses API si cambia el entorno.
4. Adaptar `.github/agent-catalog/catalog.json` al stack del repositorio.
5. Probar primero `daily_report`.
6. Probar despues `commit_analyzer`.
7. Activar por ultimo `pull_request_reviewer`.

## Siguientes pasos recomendados
- Traducir tambien el contenido de `.github/workflows/daily_report.yml` y su prompt para dejar todo consistente.
- Añadir tests simples de scripts para validar el selector y los constructores de contexto.
