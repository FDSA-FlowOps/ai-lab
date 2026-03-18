#!/usr/bin/env bash

set -euo pipefail

MAX_BYTES="${MAX_DIFF_BYTES:-120000}"
EVENT_NAME="${GITHUB_EVENT_NAME:-}"
EVENT_PATH="${GITHUB_EVENT_PATH:-}"
HEAD_SHA="${GITHUB_SHA:-HEAD}"
REPOSITORY="${GITHUB_REPOSITORY:-unknown}"
REF_NAME="${GITHUB_REF_NAME:-unknown}"

BASE_SHA=""
TITLE=""
BODY=""

if [[ "$EVENT_NAME" == "pull_request" ]]; then
  BASE_SHA="$(jq -r '.pull_request.base.sha' "$EVENT_PATH")"
  HEAD_SHA="$(jq -r '.pull_request.head.sha' "$EVENT_PATH")"
  TITLE="$(jq -r '.pull_request.title // ""' "$EVENT_PATH")"
  BODY="$(jq -r '.pull_request.body // ""' "$EVENT_PATH")"
elif [[ "$EVENT_NAME" == "push" ]]; then
  BASE_SHA="$(jq -r '.before // ""' "$EVENT_PATH")"
  TITLE="$(jq -r '.head_commit.message // ""' "$EVENT_PATH")"
  BODY=""
else
  BASE_SHA="$(git rev-parse HEAD^ 2>/dev/null || true)"
fi

if [[ -z "$BASE_SHA" || "$BASE_SHA" =~ ^0+$ ]]; then
  BASE_SHA="$(git rev-parse HEAD^ 2>/dev/null || git rev-parse HEAD)"
fi

RANGE="$BASE_SHA...$HEAD_SHA"
FULL_DIFF_FILE="full-diff.txt"
TRUNCATED_DIFF_FILE="diff-truncated.txt"

git diff --name-only "$RANGE" > changed-files.txt || true
git diff --unified=3 "$RANGE" > "$FULL_DIFF_FILE" || true

FULL_SIZE="$(wc -c < "$FULL_DIFF_FILE" | tr -d ' ')"
head -c "$MAX_BYTES" "$FULL_DIFF_FILE" > "$TRUNCATED_DIFF_FILE"

TRUNCATED="false"
if [[ "$FULL_SIZE" -gt "$MAX_BYTES" ]]; then
  TRUNCATED="true"
fi

{
  echo "# Metadata"
  echo "Repository: $REPOSITORY"
  echo "Event: $EVENT_NAME"
  echo "Ref: $REF_NAME"
  echo "Base SHA: $BASE_SHA"
  echo "Head SHA: $HEAD_SHA"
  echo "Diff range: $RANGE"
  echo "Diff truncated: $TRUNCATED"
  echo "Diff max bytes: $MAX_BYTES"
  echo

  echo "# Title"
  printf '%s\n' "$TITLE"
  echo

  echo "# Body"
  printf '%s\n' "$BODY"
  echo

  echo "# Commit list"
  git log --oneline "$RANGE" || true
  echo

  echo "# Commit messages"
  git log --format='%H%n%s%n%b%n---' "$RANGE" || true
  echo

  echo "# Changed files"
  cat changed-files.txt
  echo

  echo "# Name status"
  git diff --name-status "$RANGE" || true
  echo

  echo "# Diff stat"
  git diff --stat "$RANGE" || true
  echo

  echo "# Extension stats"
  if [[ -s changed-files.txt ]]; then
    awk '
      {
        n=split($0, parts, "/");
        file=parts[n];
        ext="(no extension)";
        if (file ~ /\./) {
          sub(/^.*\./, ".", file);
          ext=file;
        }
        count[ext]++;
      }
      END {
        for (k in count) {
          printf "%s: %d\n", k, count[k];
        }
      }
    ' changed-files.txt | sort
  fi
  echo

  if [[ "$TRUNCATED" == "true" ]]; then
    echo "# Diff warning"
    echo "[WARNING] Unified diff was truncated to $MAX_BYTES bytes."
    echo "[WARNING] Treat the analysis as partial coverage."
    echo
  fi

  echo "# Unified diff"
  cat "$TRUNCATED_DIFF_FILE"
} > commit-context.txt
