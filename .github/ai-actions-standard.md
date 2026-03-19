# Estandar de Actions con IA

## Proposito
Este documento define el estandar base de GitHub Actions impulsadas por IA que pueden adoptarse en distintos repositorios como marco comun.

El alcance actual incluye tres workflows:
- `commit_analyzer`
- `pull_request_reviewer`
- `daily_report`

El objetivo es que cada workflow tenga una responsabilidad clara, predecible y reutilizable entre repositorios con la menor personalizacion posible.

## Principios de Diseno
- Un workflow, una responsabilidad principal.
- Priorizar contexto explicito del repositorio frente a suposiciones ocultas.
- Generar salidas legibles para personas y reutilizables por automatizaciones.
- Evitar sobreorquestacion en cambios pequenos.
- Posponer la invocacion multiagente hasta que la logica de seleccion sea estable y justificable.

## Catalogo de Workflows

## `commit_analyzer`
### Proposito
Analizar un commit o un push y producir un resumen conciso de los cambios implementados.

### Alcance
- Resumir los cambios tecnicos mas relevantes del rango de commits.
- Clasificar el cambio cuando aporte valor, por ejemplo `feature`, `fix`, `refactor`, `docs`, `test`, `chore`.
- Evaluar la calidad del titulo del commit o del mensaje principal del push.
- Generar una entrada corta reutilizable en changelog.

### No Objetivos Explicitos
- No decide que agentes de revision deben invocarse.
- No realiza una revision profunda de codigo.
- No modifica descripciones de pull request.

### Trigger
- `push`
- `workflow_dispatch`

### Entradas
- Rango de commits obtenido del evento de GitHub.
- Ficheros modificados.
- Diff unificado truncado cuando sea necesario.
- Mensajes de commit y metadatos.

### Salidas
- Resumen en Markdown en el job summary.
- Artifact con el contexto generado y el analisis.
- Artifact opcional con una entrada de changelog.

### Forma Estandar de la Salida
- Alcance analizado
- Clasificacion
- Resumen de cambios
- Calidad del mensaje de commit
- Entrada de changelog

## `pull_request_reviewer`
### Proposito
Analizar una pull request, preparar su descripcion a partir de una plantilla estandar y producir un resumen estructurado para revision.

### Alcance
- Construir el contexto de la PR a partir de titulo, cuerpo, ficheros modificados, lista de commits y diff.
- Rellenar o normalizar la descripcion de la PR segun una plantilla compartida.
- Generar un resumen orientado a revision para mantenedores.
- Determinar que agentes especializados aplicarian a esa PR.

### Alcance Futuro
- Invocar los agentes seleccionados y agregar sus salidas.

### No Objetivos Explicitos Para V1
- No invocar aun multiples agentes.
- No sobrerrevisar pull requests triviales.
- No bloquear merges de forma autonoma.

### Trigger
- `pull_request` en `opened`, `synchronize`, `reopened`
- `workflow_dispatch` opcional

### Entradas
- Metadatos de la PR.
- Plantilla de PR.
- Ficheros modificados y diff.
- Catalogo de agentes local o externo de forma opcional.

### Salidas
- Descripcion de PR alineada con la plantilla.
- Resumen de revision en job summary y opcionalmente como comentario en la PR.
- Lista explicita de agentes de revision seleccionados.

### Forma Estandar de la Salida
- Resumen de la PR
- Cambios clave
- Riesgos o pruebas faltantes
- Agentes seleccionados
- Recomendacion de revision

### Politica de Seleccion de Agentes
La seleccion de agentes pertenece aqui, no en `commit_analyzer`.

Reglas base recomendadas:
- Seleccionar por area afectada, no solo por numero de ficheros.
- Aplicar un numero maximo de agentes por PR.
- Usar prioridad para recortar agentes sobrantes.
- Mantener las PR pequenas con una revision ligera.
- Reservar la ejecucion multiagente para cambios medianos o de mayor impacto.

Umbrales iniciales sugeridos:
- PR `tiny`: hasta 1 agente
- PR `small`: hasta 2 agentes
- PR `medium`: hasta 3 agentes
- PR `large`: hasta 4 agentes si realmente hay varias areas afectadas

## Reglas y Restricciones de Agentes
Esta seccion define como deben seleccionarse, limitarse y configurarse los agentes de revision para evitar sobreactivacion, duplicidad y coste innecesario.

### Objetivos
- Seleccionar solo los agentes que aporten valor real a la PR.
- Reducir revisiones redundantes en cambios pequenos.
- Mantener una politica estable y auditable entre repositorios.
- Permitir excepciones controladas para rutas o areas criticas.

### Principios Base
- Los agentes se seleccionan por impacto tecnico y area afectada, no por coincidencia bruta de muchos ficheros.
- La seleccion debe ser determinista siempre que sea posible.
- Debe existir un limite maximo de agentes por ejecucion.
- Debe evitarse activar agentes solapados cuando uno ya cubre la misma responsabilidad principal.
- Las rutas criticas pueden elevar prioridad o saltarse limites concretos.

### Reglas Minimas Obligatorias
- `max_agents_per_pr`: numero maximo de agentes a seleccionar para una PR.
- `agent_priority`: prioridad numerica para ordenar candidatos.
- `min_lines_changed`: umbral minimo de lineas modificadas para activar agentes secundarios.
- `min_files_changed`: umbral minimo de ficheros modificados para activar agentes secundarios.
- `exclusive_groups`: grupos de agentes mutuamente excluyentes.
- `critical_paths`: rutas que pueden forzar la inclusion de un agente.
- `small_pr_policy`: reglas especiales para PR pequenas o triviales.

### Politica Recomendada por Tamano de PR
- `tiny`
  - Hasta 10 lineas modificadas o 1 fichero pequeno.
  - Maximo 1 agente.
  - Solo agentes primarios.
- `small`
  - Hasta 40 lineas modificadas o pocos ficheros relacionados.
  - Maximo 2 agentes.
  - Agentes secundarios solo si el area es sensible.
- `medium`
  - Hasta 200 lineas modificadas o varias piezas relacionadas.
  - Maximo 3 agentes.
  - Se permite combinacion de agentes por stack y area.
- `large`
  - Mas de 200 lineas o varias areas claramente afectadas.
  - Maximo 4 agentes.
  - Se permiten excepciones justificadas para rutas criticas.

### Reglas de Prioridad
- Cada agente debe tener una prioridad numerica.
- Los candidatos se ordenan de mayor a menor prioridad.
- Si el numero de candidatos supera el limite configurado, se recorta la lista por prioridad.
- Los agentes forzados por rutas criticas se evalúan antes del recorte final.

### Reglas de Exclusividad
Se deben definir grupos de agentes que no deban activarse a la vez salvo excepcion explicita.

Ejemplos:
- `backend-general` y `dotnet-agent`
- `api-general` y `agent-api`
- `frontend-general` y `react-agent`

Regla recomendada:
- Si dos agentes pertenecen al mismo grupo exclusivo, se selecciona solo el de mayor prioridad.

### Reglas para Rutas Criticas
Algunas rutas pueden justificar una revision mas estricta aunque la PR sea pequena.

Ejemplos tipicos:
- `.github/workflows/**`
- `auth/**`
- `security/**`
- `api/contracts/**`
- `infra/**`

Comportamiento recomendado:
- Forzar un agente especifico.
- Permitir un agente adicional aunque la PR sea `tiny` o `small`.
- Marcar la PR como de mayor sensibilidad.

### Reglas para Cambios Triviales
Debe existir una politica para evitar sobrerreaccion en cambios de bajo impacto.

Ejemplos de cambios triviales:
- Correcciones menores de texto o comentarios.
- Renombres locales sin impacto funcional.
- Ajustes de formato o documentacion.

Comportamiento recomendado:
- No activar agentes especializados.
- O activar como maximo un agente primario si la ruta es sensible.

## Configuracion de Agentes
La configuracion debe poder mantenerse en un catalogo local o compartido y debe ser legible por scripts de seleccion.

### Campos Recomendados por Agente
- `id`: identificador unico.
- `description`: descripcion corta de su responsabilidad.
- `match`: patrones de ruta que lo hacen candidato.
- `priority`: prioridad numerica.
- `group`: grupo funcional o de exclusividad.
- `primary`: indica si es agente principal del area.
- `min_lines_changed`: umbral minimo para activarlo.
- `min_files_changed`: umbral minimo para activarlo.
- `critical_paths`: rutas que fuerzan su activacion.
- `max_pr_size`: tamano maximo de PR donde tiene sentido usarlo, si aplica.
- `enabled`: permite activar o desactivar el agente sin borrar su configuracion.

### Ejemplo de Configuracion
```json
{
  "policy": {
    "max_agents_per_pr": 3,
    "small_pr_max_agents": 1,
    "tiny_pr_max_agents": 1,
    "medium_pr_max_agents": 3,
    "large_pr_max_agents": 4,
    "tiny_max_changed_lines": 10,
    "small_max_changed_lines": 40,
    "medium_max_changed_lines": 200
  },
  "agents": [
    {
      "id": "dotnet-agent",
      "description": "Revision tecnica de cambios .NET y C#.",
      "match": ["**/*.cs", "**/*.csproj", "**/*.sln"],
      "priority": 80,
      "group": "backend-dotnet",
      "primary": true,
      "min_lines_changed": 1,
      "min_files_changed": 1,
      "critical_paths": [],
      "enabled": true
    },
    {
      "id": "agent-api",
      "description": "Revision de contratos, endpoints y cambios de API.",
      "match": ["**/Controllers/**", "**/Endpoints/**", "**/*.http"],
      "priority": 90,
      "group": "backend-api",
      "primary": true,
      "min_lines_changed": 5,
      "min_files_changed": 1,
      "critical_paths": ["api/contracts/**"],
      "enabled": true
    },
    {
      "id": "workflow-agent",
      "description": "Revision de workflows y automatizaciones.",
      "match": [".github/workflows/**", ".github/scripts/**"],
      "priority": 95,
      "group": "automation",
      "primary": true,
      "min_lines_changed": 1,
      "min_files_changed": 1,
      "critical_paths": [".github/workflows/**"],
      "enabled": true
    }
  ]
}
```

### Orden Recomendado de Evaluacion
1. Calcular tamano de PR: lineas y ficheros modificados.
2. Detectar rutas criticas.
3. Obtener agentes candidatos por patrones `match`.
4. Filtrar agentes deshabilitados.
5. Aplicar umbrales minimos por lineas y ficheros.
6. Resolver exclusividad por `group`.
7. Añadir o mantener agentes forzados por `critical_paths`.
8. Ordenar por `priority`.
9. Recortar por `max_agents_per_pr`.
10. Publicar el resultado con la justificacion de cada agente seleccionado.

### Salida Esperada del Selector de Agentes
El selector de agentes debe producir una salida estructurada y explicable.

Campos recomendados:
- Tamano estimado de la PR: `tiny`, `small`, `medium`, `large`
- Areas detectadas
- Agentes candidatos
- Agentes descartados y motivo
- Agentes finales seleccionados
- Reglas aplicadas o excepciones activadas

### Buenas Practicas de Configuracion
- Empezar con pocos agentes y reglas simples.
- Priorizar agentes primarios antes que especializados redundantes.
- Revisar falsos positivos y falsos negativos despues de varias PR.
- No meter logica compleja en prompts si puede resolverse con reglas deterministas.
- Versionar el catalogo y documentar cambios de politica.

## `daily_report`
### Proposito
Generar un resumen diario de la actividad del repositorio en las ultimas 24 horas.

### Alcance
- Resumir commits recientes y ficheros modificados.
- Destacar cambios relevantes de codigo, workflows o documentacion.
- Senalar riesgos visibles o areas que requieren atencion.
- Proponer siguientes pasos breves.

### No Objetivos Explicitos
- No es un analizador de commits.
- No es un revisor de pull requests.
- No debe intentar una revision profunda por fichero.

### Trigger
- `schedule`
- `workflow_dispatch`

### Entradas
- Historial reciente de commits.
- Ficheros modificados en las ultimas 24 horas.
- Contexto general del repositorio.

### Salidas
- Informe diario en Markdown en el job summary.
- Artifact opcional con el informe generado.

### Forma Estandar de la Salida
- Resumen ejecutivo
- Cambios relevantes
- Riesgos o alertas
- Siguientes pasos

## Patron Compartido de Implementacion
Todos los workflows deberian seguir, cuando sea posible, la misma estructura:

1. Hacer checkout del repositorio con historial suficiente.
2. Construir un fichero de contexto explicito.
3. Ejecutar la accion de IA contra ese contexto.
4. Persistir la salida principal en Markdown.
5. Publicar la salida en `GITHUB_STEP_SUMMARY`.
6. Subir artifacts cuando aporten valor para depuracion o reutilizacion.

## Activos Estandar del Repositorio
Cada repositorio que adopte este estandar deberia mantener de forma explicita estos activos:
- Workflows en `.github/workflows/`
- Prompts en `.github/prompts/`
- Scripts auxiliares en `.github/scripts/`
- Catalogo de agentes opcional en `.github/agent-catalog/`

## Guia de Despliegue
Orden recomendado de implantacion:

1. `daily_report`
2. `commit_analyzer`
3. `pull_request_reviewer`
4. Invocacion de agentes dentro de `pull_request_reviewer`

Este orden mantiene el sistema observable y evita introducir orquestacion de revision antes de que las senales base sean fiables.

## Decisiones Recogidas
- `commit_analyzer` queda limitado a resumen y analisis orientado a changelog.
- La seleccion de agentes se mueve a `pull_request_reviewer`.
- `pull_request_reviewer` sera responsable de completar la plantilla de PR y de la futura orquestacion de agentes.
- `daily_report` se mantiene como resumen del repositorio de las ultimas 24 horas.

## Preguntas Abiertas
- Debe `pull_request_reviewer` actualizar directamente el cuerpo de la PR o solo proponer una version generada para confirmacion?
- Deben vivir los catalogos de agentes en cada repo o en un repositorio compartido externo?
- Que rutas se consideran lo bastante criticas como para saltarse los limites normales de agentes?
