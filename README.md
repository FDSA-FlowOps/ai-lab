# Plantilla de Actions con IA

Esta carpeta de automatizacion esta preparada para reutilizarse en otros repositorios con el menor numero posible de cambios.

La idea base es simple:
- copiar `.github/` al nuevo repositorio
- configurar el secret `AZURE_OPENAI_API_KEY`
- revisar el endpoint de Responses API
- adaptar el catalogo de agentes al stack del repo

Con eso ya deberias poder ejecutar la plantilla.

## Que incluye la plantilla

### Workflows base
- [Analizador de Commits](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/ai_commit_analyzer.yml)
- [Revisor de Pull Requests](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/pr_reviewer.yml)
- [Informe Diario del Repositorio](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/daily_report.yml)

### Configuracion y reglas
- [Estandar funcional](C:/Users/fernando_campos/Repositories/ai-lab/.github/ai-actions-standard.md)
- [Catalogo de agentes](C:/Users/fernando_campos/Repositories/ai-lab/.github/agent-catalog/catalog.json)
- [Plantilla de PR](C:/Users/fernando_campos/Repositories/ai-lab/.github/pull_request_template.md)

### Prompts
- [Prompt de Commit Analyzer](C:/Users/fernando_campos/Repositories/ai-lab/.github/prompts/commit-analyzer.md)
- [Prompt de descripcion de PR](C:/Users/fernando_campos/Repositories/ai-lab/.github/prompts/pull-request-description.md)
- [Prompt de code review de PR](C:/Users/fernando_campos/Repositories/ai-lab/.github/prompts/pull-request-code-review.md)

### Scripts
- [Contexto de commits](C:/Users/fernando_campos/Repositories/ai-lab/.github/scripts/build-commit-context.sh)
- [Contexto de PR](C:/Users/fernando_campos/Repositories/ai-lab/.github/scripts/build-pr-context.sh)
- [Selector de agentes](C:/Users/fernando_campos/Repositories/ai-lab/.github/scripts/select-agents.js)

### Workflow opcional
- [Ejemplo de GitHub Models](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/github_models.yml)
  No forma parte del nucleo de la plantilla. Es solo un ejemplo de llamada a GitHub Models con `PAT_MODELS_TOKEN`.

## Responsabilidad de cada workflow

### `commit_analyzer`
Se ejecuta sobre `push` y `workflow_dispatch`.

Hace esto:
- construye el contexto del commit o rango de commits
- resume el cambio
- clasifica el cambio con una taxonomia ligera
- evalua la calidad del mensaje de commit
- genera una entrada de changelog

No hace esto:
- no selecciona agentes de revision
- no revisa una PR
- no comenta codigo en GitHub

Artefactos principales:
- `commit-context.txt`
- `change-metrics.json`
- `change-numstat.txt`
- `commit-analysis.md`
- `changelog-entry.md`

### `pull_request_reviewer`
Se ejecuta sobre `pull_request` en `opened`, `synchronize` y `reopened`.

Esta dividido en tres jobs:

1. `prepare_pr_context`
   Construye el contexto, calcula metricas y selecciona agentes.

2. `update_pr_description`
   Genera una descripcion propuesta de la PR y actualiza el cuerpo dentro de un bloque gestionado.

3. `review_pr_code`
   Ejecuta una revision tecnica del diff, propone labels y publica una review en GitHub con comentarios inline cuando la linea del hallazgo es valida.

Hace esto:
- lee la plantilla de PR
- construye contexto de PR
- calcula agentes candidatos segun reglas
- actualiza la descripcion de la PR
- aplica labels de tipo y area
- publica una revision tecnica de GitHub

No hace esto:
- no modifica commits
- no revisa codigo fuera del diff de la PR
- no ejecuta aun multiples agentes de IA en paralelo

Artefactos principales:
- `pr-context.txt`
- `pr-changed-files.txt`
- `change-metrics.json`
- `selected-agents.json`
- `selected-agents.md`
- `pr-description.md`
- `pr-review-findings.json`

### `daily_report`
Se ejecuta por `schedule` y `workflow_dispatch`.

Hace esto:
- resume los commits recientes de las ultimas 24 horas
- destaca cambios relevantes
- senala riesgos o alertas
- propone siguientes pasos

No hace esto:
- no sustituye el analisis de commits
- no sustituye la revision tecnica de PR

Artefacto principal:
- `daily-report.md`

## Como funciona la seleccion de agentes

La logica real de seleccion esta en [select-agents.js](C:/Users/fernando_campos/Repositories/ai-lab/.github/scripts/select-agents.js) y la configuracion vive en [catalog.json](C:/Users/fernando_campos/Repositories/ai-lab/.github/agent-catalog/catalog.json).

La evaluacion sigue este orden:
1. leer ficheros modificados
2. leer metricas del diff
3. clasificar el tamano del cambio: `tiny`, `small`, `medium`, `large`
4. detectar agentes candidatos por `match`
5. forzar agentes por `critical_paths`
6. filtrar agentes deshabilitados
7. aplicar minimos de lineas y ficheros
8. aplicar restricciones para PR pequenas
9. resolver exclusividad por `group`
10. ordenar por `priority`
11. recortar por limites maximos

Esto permite que una PR pequena no active demasiados agentes.

## Que se puede configurar

### Configuracion minima obligatoria
En casi cualquier repo solo necesitas revisar:
- el secret `AZURE_OPENAI_API_KEY`
- el endpoint `responses-api-endpoint` en los workflows
- el catalogo de agentes

### Secret requerido
- `AZURE_OPENAI_API_KEY`

Sin este secret, los workflows base se auto-omiten de forma segura.

### Endpoint del modelo
Los workflows usan hoy un endpoint de Azure OpenAI Responses API. Si cambias de entorno, debes actualizar ese valor en:
- [ai_commit_analyzer.yml](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/ai_commit_analyzer.yml)
- [pr_reviewer.yml](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/pr_reviewer.yml)
- [daily_report.yml](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/daily_report.yml)

### Catalogo de agentes
Los campos mas habituales son:
- `policy.max_agents_per_pr`
- `policy.max_agents_by_size`
- `policy.tiny_max_changed_lines`
- `policy.small_max_changed_lines`
- `policy.medium_max_changed_lines`
- `policy.tiny_primary_only`
- `policy.small_primary_only`
- `policy.critical_paths_override_limits`

Por agente:
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

### Plantilla de PR
Si tu equipo usa otra estructura de PR, adapta:
- [pull_request_template.md](C:/Users/fernando_campos/Repositories/ai-lab/.github/pull_request_template.md)
- [pull-request-description.md](C:/Users/fernando_campos/Repositories/ai-lab/.github/prompts/pull-request-description.md)

### Taxonomia de labels
Si tu equipo ya tiene labels estandar, revisa el mapa de colores y nombres en:
- [pr_reviewer.yml](C:/Users/fernando_campos/Repositories/ai-lab/.github/workflows/pr_reviewer.yml)

## Como mover esta plantilla a otro repo

### Opcion minima
1. Copia toda la carpeta `.github/`.
2. Crea el secret `AZURE_OPENAI_API_KEY`.
3. Ajusta el endpoint de Azure OpenAI si cambia el entorno.
4. Revisa [catalog.json](C:/Users/fernando_campos/Repositories/ai-lab/.github/agent-catalog/catalog.json) para que coincida con el stack del nuevo repo.
5. Revisa [pull_request_template.md](C:/Users/fernando_campos/Repositories/ai-lab/.github/pull_request_template.md) si tu equipo usa otra plantilla.

### Opcion recomendada
1. Copia `.github/`.
2. Configura `AZURE_OPENAI_API_KEY`.
3. Ajusta el endpoint.
4. Adapta el catalogo de agentes.
5. Ejecuta primero `daily_report` manualmente.
6. Haz un `push` de prueba para validar `commit_analyzer`.
7. Abre una PR de prueba para validar `pull_request_reviewer`.

## Como probar la plantilla

### Prueba de `commit_analyzer`
1. Haz un cambio pequeno.
2. Haz `commit`.
3. Haz `push`.
4. Revisa el workflow `Analizador de Commits`.

### Prueba de `pull_request_reviewer`
1. Abre una PR.
2. Haz `push` a la misma rama.
3. Revisa el workflow `Revisor de Pull Requests`.
4. Comprueba:
   - el bloque gestionado de descripcion
   - las labels aplicadas
   - la review tecnica publicada
   - los artefactos `selected-agents.json` y `pr-review-findings.json`

### Prueba de `daily_report`
1. Ejecutalo por `workflow_dispatch`.
2. Revisa el resumen del job y el `daily-report.md`.

## Recomendaciones para equipos
- empieza con pocos agentes y reglas simples
- evita prompts con demasiada logica si esa logica puede vivir en scripts
- revisa falsos positivos y falsos negativos del selector despues de varias PR
- no fuerces `request_changes` salvo que el modelo sea suficientemente estable para tu repositorio
- mantén una taxonomia estable de labels

## Ficheros opcionales o no nucleares
- `github_models.yml`
  Ejemplo aislado para GitHub Models. Puede eliminarse sin afectar la plantilla principal.

## Estado actual de la plantilla
La plantilla esta pensada para ser funcional y portable, pero aun hay mejoras razonables:
- añadir tests a los scripts
- unificar del todo el idioma de todos los ficheros restantes
- parametrizar el endpoint de Azure OpenAI via variables del repo
- endurecer la validacion de hallazgos antes de publicar reviews inline
