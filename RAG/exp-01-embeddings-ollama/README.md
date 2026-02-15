# exp-01-embeddings-ollama

Experimento independiente para generar embeddings con Ollama local, calcular cosine similarity en local y detectar duplicados semanticos.

## Requisitos

- .NET 10 SDK
- Ollama corriendo localmente en `http://localhost:11434`
- Modelo instalado:

```bash
ollama pull bge-m3
```

## Configuracion

Valores por defecto en `appsettings.json`:

- `OllamaBaseUrl`: `http://localhost:11434`
- `OllamaModel`: `bge-m3`
- `DupThreshold`: `0.85`
- `TopK`: `5`
- `HttpTimeoutSeconds`: `30`

Variables de entorno soportadas (ver `.env.example`):

- `OLLAMA_BASE_URL`
- `OLLAMA_MODEL`
- `DUP_THRESHOLD`
- `TOP_K`
- `HTTP_TIMEOUT_SECONDS`

En PowerShell (ejemplo):

```powershell
$env:OLLAMA_BASE_URL="http://localhost:11434"
$env:OLLAMA_MODEL="bge-m3"
$env:DUP_THRESHOLD="0.85" # tambien acepta 0,85
$env:TOP_K="5"
$env:HTTP_TIMEOUT_SECONDS="30"
```

## Ejecutar

Desde este folder:

```bash
dotnet restore
dotnet build
```

### 1) Embedding de un texto

```bash
dotnet run -- embed "hola mundo de embeddings"
```

### 2) Modo interactivo (recomendado para pruebas de equipo)

Inicia el modo interactivo:

```bash
dotnet run
```

Veras algo como:

```text
Modo interactivo activado.
Modelo: bge-m3
Base URL: http://localhost:11434
TOP_K: 5 | DUP_THRESHOLD: 0.85
rag>
```

Comandos disponibles dentro del prompt:

```text
embed <texto>
similar <texto>
similar-th <texto>
duplicates
help
exit
```

Prueba guiada (copy/paste uno por uno):

```text
help
embed necesito resetear mi password
similar-th quiero cancelar mi plan antes de la renovacion
similar quiero cancelar mi plan antes de la renovacion
duplicates
exit
```

`similar-th` aplica filtro por umbral (`score >= DUP_THRESHOLD`) y es el comando recomendado para demos.

### 3) Salida esperada de `embed` (ejemplo)

```text
Model: bge-m3
Dimension: 768
Preview (primeros 8 valores):
0.013421, -0.002113, ...
```

### 4) Top K similares para una query (modo no interactivo)

```bash
dotnet run -- similar "quiero cancelar mi plan antes de la renovacion"
dotnet run -- similar --threshold-only "quiero cancelar mi plan antes de la renovacion"
```

Salida esperada (ejemplo):

```text
Top 5 similares para: "quiero cancelar mi plan antes de la renovacion"
1. 0.9521 | Necesito dar de baja el plan antes de que se renueve.
2. 0.9103 | Quiero cancelar la suscripcion antes de la renovacion.
...
```

### 5) Deteccion de duplicados semanticos

```bash
dotnet run -- duplicates
```

Salida esperada (ejemplo):

```text
Deteccion de duplicados semanticos (threshold >= 0.85)
[0.9632]
A: Me cobraron dos veces la misma reserva.
B: La factura tiene un cargo duplicado en la tarjeta.
```

## Como interpretar scores (guia rapida para comparar modelos)

- `>= 0.85`: muy alta similitud; normalmente para parafrasis o intencion casi identica.
- `0.70 - 0.84`: similitud media-alta; suele haber relacion tematica clara.
- `0.50 - 0.69`: relacion debil o parcial; revisar manualmente.
- `< 0.50`: poca relacion semantica en este dataset.

Recomendacion para pruebas del equipo:

1. Ejecutar `similar-th` con la misma query en cada modelo.
2. Comparar si el top 1 y top 2 son realmente relevantes.
3. Revisar el gap entre top relevantes y el resto (mejor separacion = mejor modelo para este caso).
4. Ajustar `DUP_THRESHOLD` en pasos de `0.05` (`0.80`, `0.85`, `0.90`) y observar precision vs recall.

## Comportamiento ante errores

Si Ollama no esta disponible o hay timeout, el programa imprime error util y termina con `exit code != 0`.

Ejemplo:

```text
[ERROR] No se pudo conectar a Ollama en 'http://localhost:11434/'. Asegura que este corriendo.
```
