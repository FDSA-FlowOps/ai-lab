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

## Ejemplo de payload
```json
{
  "event_id": "evt_123",
  "event_type": "payment.succeeded",
  "created_at": "2026-02-15T10:00:00Z",
  "data": {
    "order_id": "ord_9",
    "amount": 1999
  }
}
```

## Politica de reintentos
El sistema reintenta con backoff exponencial por hasta 24 horas.
Secuencia recomendada: 1 min, 5 min, 15 min, 1 h, 6 h, 24 h.

## Observabilidad
Registrar latencia de procesamiento, codigo HTTP y errores de validacion.
Exponer dashboard con tasa de entrega, fallos por tipo y endpoints mas inestables.
