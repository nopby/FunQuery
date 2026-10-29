#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT/deploy/compose.yaml"
SEED_FILE="$ROOT/db/seed.sql"
CONNECTION_STRING='Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=postgres'
ACTION="${1:-start}"

if command -v podman-compose >/dev/null 2>&1; then
  COMPOSE=(podman-compose)
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE=(docker-compose)
elif command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then
  COMPOSE=(podman compose)
elif command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
else
  echo "No Compose provider found. Install Podman with a compose provider, or Docker Compose." >&2
  exit 1
fi

compose() {
  "${COMPOSE[@]}" -f "$COMPOSE_FILE" "$@"
}

wait_for_postgres() {
  for _ in {1..30}; do
    if compose exec -T postgres pg_isready -h 127.0.0.1 -U postgres -d postgres >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done

  echo "PostgreSQL did not become ready within 60 seconds." >&2
  echo "Check logs with: ${COMPOSE[*]} -f $COMPOSE_FILE logs postgres" >&2
  return 1
}

start_postgres() {
  compose up -d
  wait_for_postgres
  echo "PostgreSQL is ready."
  printf "  export FUNQUERY_POSTGRES='%s'\n" "$CONNECTION_STRING"
}

seed_postgres() {
  compose exec -T postgres \
    psql -U postgres -d postgres -v ON_ERROR_STOP=1 -f - < "$SEED_FILE"
  echo "Seeded orders with 5 rows."
}

case "$ACTION" in
  start)
    start_postgres
    ;;
  stop)
    compose down --remove-orphans
    echo "PostgreSQL stopped; data volume kept."
    ;;
  reset)
    compose down -v --remove-orphans
    start_postgres
    seed_postgres
    ;;
  seed)
    seed_postgres
    ;;
  status)
    compose ps
    ;;
  *)
    echo "Usage: $0 {start|stop|reset|seed|status}" >&2
    exit 2
    ;;
esac
