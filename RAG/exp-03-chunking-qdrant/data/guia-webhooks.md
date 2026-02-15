# Guia Tecnica de Webhooks

## Resumen
Los webhooks permiten notificar eventos en tiempo real a sistemas externos.
Esta guia cubre registro de endpoints, firma, reintentos y observabilidad.

## Registro del endpoint
Para registrar un endpoint se requiere URL HTTPS publica y certificado valido.
El endpoint debe aceptar metodo POST y responder con codigo 2xx cuando procese correctamente.

## Seguridad de firma
### Cabecera de firma
Cada evento incluye cabecera `X-Signature` con HMAC SHA-256.
La clave compartida se obtiene desde panel de integraciones.

### Verificacion
El receptor debe reconstruir payload exacto y calcular hash local.
Si la firma no coincide, retornar 401 y registrar evento como invalido.

## Estructura de eventos
- `event_id`: identificador unico del evento.
- `event_type`: tipo de evento, por ejemplo `payment.succeeded`.
- `created_at`: timestamp UTC de emision.
- `data`: objeto con informacion de negocio.

## Idempotencia
El consumidor debe guardar `event_id` para evitar procesar duplicados.
Los eventos pueden reenviarse por reintentos de red o respuestas tardias.

## Politica de reintentos
El sistema reintenta con backoff exponencial por hasta 24 horas.
Secuencia recomendada: 1 min, 5 min, 15 min, 1 h, 6 h, 24 h.

## Codigos de respuesta esperados
### 2xx
Evento confirmado y no se reintenta.

### 4xx
Error del consumidor; se analiza segun codigo y puede detener reintentos.

### 5xx o timeout
Se considera fallo transitorio y entra en politica de reintentos.

## Observabilidad
Registrar latencia de procesamiento, codigo HTTP y errores de validacion.
Exponer dashboard con tasa de entrega, fallos por tipo y endpoints mas inestables.

## Pruebas recomendadas
1. Payload valido con firma correcta.
2. Payload alterado con firma original.
3. Timeout de receptor mayor a 10 segundos.
4. Duplicado de `event_id` para validar idempotencia.

## Troubleshooting
Si hay muchos 401, revisar sincronizacion de secreto.
Si hay muchos 5xx, verificar capacidad del receptor y timeout aguas abajo.
Si hay picos de latencia, activar cola interna para desacoplar procesamiento.
