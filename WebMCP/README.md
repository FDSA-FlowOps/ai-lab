# Demo WebMCP (Web Model Context Protocol)

Pequena web de ejemplo que expone herramientas de una app de tareas mediante `navigator.modelContext`.

## Que encontre sobre el estandar (estado al 13 de febrero de 2026)

- El repositorio oficial de incubacion esta en `webmachinelearning/webmcp`.
- En la lista publica de W3C WebML CG se anuncio (30 de septiembre de 2025) la aceptacion de una propuesta WebMCP como nuevo deliverable del grupo.
- La API sigue en fase de propuesta/incubacion; por eso la demo incluye deteccion de soporte y fallback de mensaje cuando no existe `navigator.modelContext`.

## Como ejecutar

1. Sirve esta carpeta con cualquier servidor local (necesario para `type="module"`):
   - `npx serve .`
   - o `python -m http.server 8080`
2. Abre `http://localhost:3000` (o el puerto que corresponda).

`localhost` cuenta como secure context, que es importante para APIs web modernas.

## Uso en GitHub Pages

Publicar en GitHub Pages solo aloja la web; no activa por si mismo el acceso de agentes a WebMCP.

- Si abres la URL y ves "Sin soporte nativo detectado", significa que el navegador/cliente no expone `navigator.modelContext`.
- Para que un agente pueda usar estas tools necesitas un cliente compatible con WebMCP o un bridge/polyfill activo (por ejemplo, mcp-b) en ese navegador.
- La URL desplegada de este repo es: `https://lopezalbarca.github.io/WebMCP/` (responde OK con `200` para `index.html` y `app.js`).

## Que implementa la demo

- Herramientas WebMCP:
  - `add_task`
  - `list_tasks`
  - `complete_task`
- Registro en tiempo de ejecucion con:
  - `modelContext.registerTool(...)` si esta disponible
  - o `modelContext.provideContext({ tools })` como alternativa
- UI local para ver el mismo estado de tareas y un panel de log.

## Variante declarativa (sin JS de registro)

La variante declarativa esta en `declarative.html`, separada de la pagina JS.
Usa atributos HTML en formularios (sin usar `navigator.modelContext`):

- Esta variante es experimental y puede cambiar.
- Si el navegador no reconoce esos atributos, la pagina sigue funcionando como
  HTML normal.
- Atributos usados en la demo: `toolname`, `tooldescription`,
  `toolpropdescription`, `toolautosubmit`.
- Las acciones (`add_task`, `list_tasks`, `complete_task`) se resuelven con
  JavaScript local en la misma `declarative.html`, sin redireccion.
- El estado se guarda en `localStorage`.
- Cada `submit` declarativo responde al agente con `event.respondWith(...)` para
  evitar resultados `null` en tool calls.

## Archivos

- `index.html`: demo JS imperativa (`registerTool`/`provideContext`).
- `declarative.html`: demo declarativa por atributos HTML.
- `styles.css`: estilos responsive.
- `app.js`: estado de tareas y capa WebMCP.

## Fuentes

- https://github.com/webmachinelearning/webmcp
- https://github.com/webmachinelearning/webmcp/docs/proposal.md
- https://github.com/webmachinelearning/webmcp/issues/22
- https://lists.w3.org/Archives/Public/public-webmachinelearning/2025Sep/0028.html
