# Politica de Reembolsos y Devoluciones

## Objetivo
Esta politica define como se gestionan devoluciones y reembolsos para compras digitales y servicios recurrentes.
El objetivo es dar reglas claras para clientes y agentes de soporte.

## Alcance
Aplica a compras realizadas en web, app movil y ventas asistidas por agentes.
No aplica a transacciones procesadas por marketplaces externos.

## Ventana de reembolso
- Compras de pago unico: hasta 7 dias calendario desde la fecha de compra.
- Suscripciones mensuales: hasta 48 horas despues de la renovacion automatica.
- Suscripciones anuales: hasta 72 horas despues de la renovacion.

## Causales validas
1. Cobro duplicado por falla transaccional.
2. Servicio no disponible por incidente mayor con impacto total.
3. Error de facturacion que incluya conceptos no contratados.
4. Incumplimiento de funcionalidad comprometida en plan contratado.

## Causales no validas
- Cambio de opinion sin uso comprobable del servicio por fuera de ventana.
- Solicitudes fuera del plazo sin evidencia de falla critica.
- Cuentas suspendidas por incumplimiento de terminos.

## Evidencias requeridas
El cliente debe aportar identificador de pedido, correo asociado y fecha aproximada.
Cuando se trate de cobro duplicado, debe incluir ultimos 4 digitos de tarjeta y captura del estado de cuenta.
Soporte puede solicitar logs de sesion o detalle del error para validar la causal.

## Flujo operativo
### Recepcion
El agente registra ticket con categoria billing-refund y prioridad segun impacto.
Debe confirmar identidad del solicitante antes de revelar datos financieros.

### Validacion
Se valida elegibilidad por ventana, causal y evidencia.
Si faltan datos, se pide informacion complementaria en un unico mensaje estructurado.

### Resolucion
Si procede, se aprueba y se genera orden de reembolso en pasarela.
Si no procede, se informa rechazo con motivo y referencia a esta politica.

## Tiempos objetivo
- Validacion inicial: 24 horas habiles.
- Aprobacion o rechazo: 48 horas habiles.
- Confirmacion de acreditacion bancaria: hasta 10 dias habiles segun emisor.

## Casos especiales
En incidentes masivos el area de operaciones puede autorizar reembolsos por lote.
En cuentas enterprise, la decision final puede requerir aprobacion de Customer Success Manager.

## Comunicacion al cliente
La respuesta debe incluir estado, motivo, numero de caso y proximo paso.
No se debe prometer fecha exacta de acreditacion si depende de terceros.

## Auditoria y trazabilidad
Cada decision de reembolso debe quedar registrada con agente, fecha y evidencia.
Se conserva historial por 24 meses para revisiones internas.
