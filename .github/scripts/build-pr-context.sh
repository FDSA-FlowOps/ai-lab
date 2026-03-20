#!/usr/bin/env bash

set -euo pipefail

MAX_BYTES="${MAX_DIFF_BYTES:-120000}"
EVENT_PATH="${GITHUB_EVENT_PATH:-}"
REPOSITORY="${GITHUB_REPOSITORY:-unknown}"

PR_NUMBER="$(jq -r '.pull_request.number // ""' "$EVENT_PATH")"
PR_TITLE="$(jq -r '.pull_request.title // ""' "$EVENT_PATH")"
PR_BODY="$(jq -r '.pull_request.body // ""' "$EVENT_PATH")"
PR_BASE_REF="$(jq -r '.pull_request.base.ref // ""' "$EVENT_PATH")"
PR_BASE_SHA="$(jq -r '.pull_request.base.sha // ""' "$EVENT_PATH")"
PR_HEAD_REF="$(jq -r '.pull_request.head.ref // ""' "$EVENT_PATH")"
PR_HEAD_SHA="$(jq -r '.pull_request.head.sha // ""' "$EVENT_PATH")"
PR_TEMPLATE_FILE=".github/pull_request_template.md"

FULL_DIFF_FILE="pr-full-diff.txt"
TRUNCATED_DIFF_FILE="pr-diff-truncated.txt"
NUMSTAT_FILE="change-numstat.txt"
METRICS_FILE="change-metrics.json"

if [[ -z "$PR_BASE_SHA" || -z "$PR_HEAD_SHA" ]]; then
  echo "Faltan PR_BASE_SHA o PR_HEAD_SHA en el evento de GitHub." >&2
  exit 1
fi

if ! git cat-file -e "$PR_BASE_SHA^{commit}" 2>/dev/null; then
  echo "El commit base $PR_BASE_SHA no esta disponible localmente. Asegura un fetch previo de la base." >&2
  exit 1
fi

if ! git cat-file -e "$PR_HEAD_SHA^{commit}" 2>/dev/null; then
  echo "El commit head $PR_HEAD_SHA no esta disponible localmente." >&2
  exit 1
fi

git diff --name-only "$PR_BASE_SHA...$PR_HEAD_SHA" > pr-changed-files.txt
git diff --unified=3 "$PR_BASE_SHA...$PR_HEAD_SHA" > "$FULL_DIFF_FILE"
git diff --numstat "$PR_BASE_SHA...$PR_HEAD_SHA" > "$NUMSTAT_FILE"

FULL_SIZE="$(wc -c < "$FULL_DIFF_FILE" | tr -d ' ')"
head -c "$MAX_BYTES" "$FULL_DIFF_FILE" > "$TRUNCATED_DIFF_FILE"

TRUNCATED="false"
if [[ "$FULL_SIZE" -gt "$MAX_BYTES" ]]; then
  TRUNCATED="true"
fi

python3 - "$NUMSTAT_FILE" "$METRICS_FILE" <<'PY'
import json
import sys

numstat_path = sys.argv[1]
metrics_path = sys.argv[2]
added = 0
deleted = 0
files_changed = 0

with open(numstat_path, "r", encoding="utf-8") as fh:
    for line in fh:
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 3:
            continue
        files_changed += 1
        if parts[0].isdigit():
            added += int(parts[0])
        if parts[1].isdigit():
            deleted += int(parts[1])

with open(metrics_path, "w", encoding="utf-8") as fh:
    json.dump(
        {
            "files_changed": files_changed,
            "lines_added": added,
            "lines_deleted": deleted,
            "total_changed_lines": added + deleted,
        },
        fh,
        indent=2,
    )
    fh.write("\n")
PY

{
  echo "# Metadatos"
  echo "Repositorio: $REPOSITORY"
  echo "PR: #$PR_NUMBER"
  echo "Base: $PR_BASE_REF ($PR_BASE_SHA)"
  echo "Head: $PR_HEAD_REF ($PR_HEAD_SHA)"
  echo "Diff truncado: $TRUNCATED"
  echo "Maximo de bytes del diff: $MAX_BYTES"
  echo

  echo "# Titulo de la PR"
  printf '%s\n' "$PR_TITLE"
  echo

  echo "# Descripcion actual de la PR"
  printf '%s\n' "$PR_BODY"
  echo

  echo "# Plantilla de PR"
  if [[ -f "$PR_TEMPLATE_FILE" ]]; then
    cat "$PR_TEMPLATE_FILE"
  else
    echo "[SIN PLANTILLA]"
  fi
  echo

  echo "# Lista de commits"
  git log --oneline "$PR_BASE_SHA...$PR_HEAD_SHA" || true
  echo

  echo "# Mensajes de commit"
  git log --format='%H%n%s%n%b%n---' "$PR_BASE_SHA...$PR_HEAD_SHA" || true
  echo

  echo "# Ficheros modificados"
  cat pr-changed-files.txt
  echo

  echo "# Estado de ficheros"
  git diff --name-status "$PR_BASE_SHA...$PR_HEAD_SHA" || true
  echo

  echo "# Numstat"
  cat "$NUMSTAT_FILE"
  echo

  echo "# Estadisticas del diff"
  git diff --stat "$PR_BASE_SHA...$PR_HEAD_SHA" || true
  echo

  if [[ "$TRUNCATED" == "true" ]]; then
    echo "# Aviso"
    echo "[AVISO] El diff unificado fue truncado a $MAX_BYTES bytes."
    echo "[AVISO] Trata esta revision como parcial."
    echo
  fi

  echo "# Diff unificado"
  cat "$TRUNCATED_DIFF_FILE"
} > pr-context.txt
