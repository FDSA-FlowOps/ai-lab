---
on:
  schedule: daily on weekdays
  workflow_dispatch:
permissions:
  contents: read
  actions: read
  pull-requests: read
safe-outputs:
  create-issue:
    title-prefix: "[Reporte Diario Repo] "
strict: false
engine:
  id: codex
  model: gpt-5.3-codex
  env:
    OPENAI_API_KEY: ${{ secrets.AZURE_OPENAI_API_KEY }}
    OPENAI_BASE_URL: ${{ secrets.FOUNDRY_BASE_URL }}
---

Genera un informe diario del estado del repositorio para mantenedores.

Objetivo:
- Entregar una vista ejecutiva del estado actual y cambios recientes.
- Destacar bloqueos, riesgos y acciones recomendadas.
- Mantener trazabilidad con enlaces directos a PRs, issues y runs.

Alcance del informe:
- Ventana temporal: ultimas 24 horas.
- Estado general del repositorio (estable, con riesgos, degradado).
- Actividad de desarrollo:
  - cantidad de commits en `main`
  - PRs abiertos, PRs fusionados en la ventana
  - issues abiertos y cerrados en la ventana
- Estado de CI:
  - workflows ejecutados en la ventana
  - fallos recientes y workflow/ejecucion afectada
- Cambios normativos/documentales relevantes si existen.

Formato requerido:
1. Resumen Ejecutivo (3-5 bullets)
2. Metricas del Dia (tabla corta)
3. Hallazgos y Riesgos (solo los relevantes)
4. Recomendaciones para Mantenedor (acciones concretas)
5. Referencias (enlaces a PRs/issues/runs)

Reglas:
- No inventar datos.
- Si falta informacion para una metrica, indicar "dato no disponible".
- Mantener tono factual y accionable.
- No incluir razonamiento interno.
