#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV="$ROOT/.venv-docs"
SITE="$ROOT/site"

cd "$ROOT"

if [ ! -x "$VENV/bin/python" ]; then
  python3 -m venv "$VENV"
fi

"$VENV/bin/pip" install --quiet -r "$ROOT/requirements-docs.txt"
"$VENV/bin/mkdocs" build --strict

if [ "${1:-}" = "--build-only" ]; then
  echo "Built documentation: $SITE"
  exit 0
fi

TAILSCALE_BIN="$(command -v tailscale || true)"
if [ -z "$TAILSCALE_BIN" ]; then
  echo "tailscale not found in PATH." >&2
  exit 1
fi

sudo "$TAILSCALE_BIN" serve --bg "$SITE"
sudo "$TAILSCALE_BIN" serve status
