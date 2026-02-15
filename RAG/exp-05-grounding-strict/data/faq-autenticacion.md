# FAQ de Autenticacion y Seguridad

## Introduccion
Este documento responde preguntas frecuentes sobre login, recuperacion de cuenta y control de sesiones.
Incluye recomendaciones operativas para equipos de soporte de primer y segundo nivel.

## Problemas de inicio de sesion
### No puedo entrar con Google
Verificar si el correo de Google coincide con el correo principal registrado.
Confirmar que la cuenta no tenga restriccion de dominio corporativo.
Si persiste error 403, invalidar token de sesion y repetir flujo OAuth.

### Error de credenciales invalidas
Validar que el usuario no tenga bloqueo temporal por intentos fallidos.
Sugerir copia y pega de contrasena para evitar errores de teclado.

### Cuenta bloqueada
Despues de cinco intentos fallidos se activa bloqueo de quince minutos.
En casos de riesgo, seguridad puede extender bloqueo y forzar cambio de contrasena.

## Recuperacion de contrasena
### No llega el correo de restablecimiento
Pedir revisar carpeta spam y filtros de seguridad.
Confirmar que el dominio no este bloqueado por proveedor de correo.
Reenviar enlace solo una vez cada cinco minutos para evitar abuso.

### Enlace expirado
Los enlaces tienen vigencia de treinta minutos.
Si expira, generar uno nuevo y anular los anteriores.

## Sesiones y dispositivos
### Me cierra sesion muy rapido
Confirmar politica de expiracion de sesion para el plan del cliente.
Revisar si hay cambios de IP o de huella de navegador que disparen reautenticacion.

### Cerrar sesion en todos los dispositivos
Disponible desde panel de seguridad del perfil.
Tambien puede ejecutarlo soporte ante indicio de compromiso de cuenta.
