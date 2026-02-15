# Manual On-Call para Incidentes Criticos

## Proposito
Este manual describe acciones de respuesta para incidentes de severidad alta.
Busca reducir tiempo de deteccion, contencion y recuperacion.

## Niveles de severidad
### Sev 1
Interrupcion total del servicio principal para la mayoria de clientes.

### Sev 2
Degradacion relevante con workaround temporal disponible.

### Sev 3
Impacto acotado en una funcionalidad no critica.

## Deteccion y activacion
Un incidente se activa por alertas automaticas o reporte masivo de clientes.
El on-call confirma impacto en menos de diez minutos.

## Checklist inicial
1. Confirmar alcance del problema y sistemas afectados.
2. Abrir canal de incidente y asignar incident commander.
3. Publicar primer status interno con hora y alcance.
4. Revisar cambios recientes en despliegues y configuracion.

## Contencion
Cuando hay deploy reciente, evaluar rollback controlado.
Si el problema es saturacion, habilitar limites de trafico o degradacion elegante.
Si la falla es externa, activar proveedor alterno cuando exista.

## Comunicacion
### Interna
Actualizar cada quince minutos con acciones, riesgos y ETA tentativa.

### Externa
Publicar estado al cliente cada treinta minutos en pagina de status.
No comunicar causa raiz definitiva hasta tener validacion tecnica.

## Escalamiento tecnico
Escalar a base de datos cuando haya latencia persistente de consultas criticas.
Escalar a red cuando haya perdida de paquetes o error TLS generalizado.
Escalar a seguridad si hay indicios de abuso o acceso no autorizado.

## Cierre del incidente
Se cierra cuando metricas vuelven a umbral normal por al menos treinta minutos.
El cierre debe incluir resumen de impacto y acciones tomadas.

## Postmortem
En 48 horas se debe publicar analisis con:
- Linea de tiempo
- Causa raiz
- Acciones correctivas
- Acciones preventivas
- Responsable y fecha compromiso

## Plantilla de actualizacion
Estado actual, impacto, mitigacion en curso, siguiente hito y hora de proxima actualizacion.
