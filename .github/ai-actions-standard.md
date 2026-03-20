# Estandar de Actions con IA

## Proposito
Este documento define el marco comun para reutilizar workflows con IA en distintos repositorios.

El objetivo del estandar es:
- separar responsabilidades con claridad
- minimizar configuracion manual por repositorio
- mantener salidas previsibles y auditables
- permitir una adopcion incremental

Los workflows base incluidos en esta plantilla son:
- `commit_analyzer`
- `pull_request_reviewer`
- `daily_report`

Ademas, puede existir algun workflow auxiliar u opcional del repositorio, pero no forma parte del nucleo de la plantilla.

## Principios
- Un workflow, una responsabilidad principal.
- El contexto debe construirse de forma explicita a partir del repositorio y del evento de GitHub.
- La IA interpreta; las reglas deterministas orquestan.
- Las salidas deben ser legibles para personas y utiles para automatizacion.
- Las decisiones de seleccion de agentes deben ser justificables.
- Los cambios pequenos no deben activar revisiones innecesarias.

## Componentes de la Plantilla

### `commit_analyzer`
#### Proposito
Analizar un `push` y generar un resumen tecnico del cambio, una clasificacion util y una entrada reutilizable en changelog.

#### Responsabilidades
- construir el contexto del commit o rango de commits
- resumir el cambio
- clasificar el cambio con una taxonomia ligera
- evaluar la calidad del mensaje de commit
- generar una entrada de changelog

#### No Responsabilidades
- no selecciona agentes de revision
- no comenta en pull requests
- no realiza code review profunda

#### Entradas
- evento `push` o `workflow_dispatch`
- historial y diff del repositorio
- contexto generado en `commit-context.txt`

#### Salidas
- `commit-analysis.md`
- `changelog-entry.md`
- `GITHUB_STEP_SUMMARY`
- artefacto `commit-analyzer`

### `pull_request_reviewer`
#### Proposito
Analizar una pull request y cubrir dos capacidades distintas dentro del mismo workflow:
- generar o actualizar la descripcion de la PR
- realizar revision tecnica del diff y publicar review comments cuando sea posible

#### Responsabilidades
- construir el contexto de la PR
- leer y reutilizar la plantilla de PR
- seleccionar agentes candidatos segun reglas
- generar una descripcion propuesta de la PR
- etiquetar la PR
- revisar tecnicamente el diff
- publicar una review de GitHub con comentarios inline validos

#### No Responsabilidades
- no invoca aun multiples agentes de revision en paralelo
- no modifica commits
- no revisa codigo fuera del diff de la PR

#### Entradas
- evento `pull_request`
- plantilla `.github/pull_request_template.md`
- diff y ficheros modificados
- reglas y catalogo de agentes

#### Salidas
- cuerpo gestionado de la PR
- labels de clasificacion
- review de GitHub con comentarios inline cuando las lineas sean validas
- `selected-agents.json`
- `selected-agents.md`
- artefactos `pr-review-context`, `pr-description`, `pr-code-review`

### `daily_report`
#### Proposito
Generar un resumen diario de la actividad del repositorio en las ultimas 24 horas.

#### Responsabilidades
- resumir actividad reciente
- destacar cambios relevantes
- senalar riesgos visibles
- proponer siguientes pasos

#### No Responsabilidades
- no sustituye la revision de PR
- no sustituye el analisis de commits
- no hace code review por fichero

#### Entradas
- evento `schedule` o `workflow_dispatch`
- historial reciente del repositorio

#### Salidas
- `daily-report.md`
- `GITHUB_STEP_SUMMARY`

## Reglas y Restricciones de Agentes
La seleccion de agentes pertenece a `pull_request_reviewer`.

### Objetivo
Evitar que una PR pequena o trivial active demasiados agentes y mantener una politica coherente y reusable.

### Politica Base
- seleccionar por area afectada, no por conteo bruto de ficheros
- calcular tamano del cambio con lineas modificadas
- ordenar por prioridad
- resolver exclusividad por grupos
- permitir excepciones por rutas criticas

### Configuracion Minima Esperada
El catalogo de agentes debe definir:
- `policy.max_agents_per_pr`
- `policy.max_agents_by_size`
- `policy.tiny_max_changed_lines`
- `policy.small_max_changed_lines`
- `policy.medium_max_changed_lines`
- `policy.tiny_primary_only`
- `policy.small_primary_only`
- `policy.critical_paths_override_limits`

Y cada agente puede definir:
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

### Flujo de Evaluacion
1. leer ficheros modificados
2. leer metricas del diff
3. clasificar tamano: `tiny`, `small`, `medium`, `large`
4. obtener candidatos por `match`
5. forzar agentes por `critical_paths`
6. filtrar agentes deshabilitados
7. aplicar minimos de lineas y ficheros
8. aplicar restricciones para PR pequenas
9. resolver exclusividad por `group`
10. ordenar por `priority`
11. recortar por limites maximos

### Salida Esperada
El selector debe producir:
- metricas del cambio
- tamano estimado de la PR
- areas detectadas
- reglas aplicadas
- agentes descartados con motivo
- agentes finales seleccionados

## Patron de Implementacion Compartido
Cuando sea posible, todos los workflows deben seguir esta estructura:

1. descargar el repositorio con historial suficiente
2. construir un fichero de contexto explicito
3. ejecutar la accion de IA sobre ese contexto
4. persistir la salida principal en un fichero
5. publicar resultado en `GITHUB_STEP_SUMMARY` o en la PR
6. subir artefactos para depuracion y reutilizacion

## Activos Base del Repositorio
La plantilla se organiza en:
- `.github/workflows/`
- `.github/prompts/`
- `.github/scripts/`
- `.github/agent-catalog/`
- `.github/pull_request_template.md`

## Configuracion Minima para Adoptar la Plantilla
Para mover esta plantilla a otro repositorio basta con:
1. copiar la carpeta `.github/`
2. configurar el secret `OPENROUTER_API_KEY`
3. revisar que los workflows apunten a `https://openrouter.ai/api/v1/responses`
4. adaptar el catalogo de agentes al stack del repositorio

## Orden Recomendado de Adopcion
1. `daily_report`
2. `commit_analyzer`
3. `pull_request_reviewer`

## Decisiones Actuales
- `commit_analyzer` se centra en resumen y changelog
- `pull_request_reviewer` concentra descripcion, etiquetado y revision tecnica
- la seleccion de agentes es determinista y configurable
- la publicacion de comentarios inline depende de que el hallazgo apunte a una linea valida del diff

## Preguntas Abiertas
- si la taxonomia de labels debe ser compartida entre repositorios o local a cada equipo
- si algunas rutas criticas deben forzar `request_changes`
- si la revision tecnica debe tolerar comentarios generales cuando un hallazgo no pueda anclarse a linea
