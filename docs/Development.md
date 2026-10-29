# Local Development

This guide starts FunQuery and PostgreSQL locally. The in-memory host does not need a database, so you
can work on the language, HTTP API, playground, and docs without PostgreSQL.

## Prerequisites

* .NET SDK 10.
* Podman with a Compose provider, or Docker Compose.
* `curl` for the examples (optional).

The compose file uses `postgres:18.3`, the version used by the PostgreSQL test suite. No local PostgreSQL
installation is required.

## Quick start

### 1. Start PostgreSQL

Windows PowerShell:

```powershell
.\scripts\dev-postgres.ps1 start
```

Unix:

```bash
./scripts/dev-postgres.sh start
```

The scripts auto-detect `podman-compose`, `docker-compose`, `podman compose`, or `docker compose`.
`start` creates:

* container `funquery-postgres`;
* named volume `funquery-postgres-data`;
* PostgreSQL on `127.0.0.1:5433` (port `5433` on the host, `5432` in the container).

On a fresh volume, `db/seed.sql` runs automatically through the image's init directory. The script waits
for `pg_isready` and prints the connection string.

### 2. Seed or reset the sample data

The seed script is idempotent: it drops and recreates `orders` with five rows.

```powershell
.\scripts\dev-postgres.ps1 seed
```

```bash
./scripts/dev-postgres.sh seed
```

Use `reset` to remove the container and its volume, start a fresh database, and seed it:

```powershell
.\scripts\dev-postgres.ps1 reset
```

```bash
./scripts/dev-postgres.sh reset
```

`reset` is destructive. `stop` removes the container but keeps the volume.

### 3. Configure the connection

Copy the example file and load it, or set the variable directly:

```bash
cp .env.example .env
set -a
source .env
set +a
```

```powershell
Copy-Item .env.example .env
$env:FUNQUERY_POSTGRES = 'Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=postgres'
```

The connection string contains semicolons, so quote it in a shell. `.env` is ignored by git;
`.env.example` is the committed template.

### 4. Run the in-memory host

```bash
dotnet run --project hosts/FunQuery.Server
```

Open:

* <http://localhost:5000/playground/> — query playground;
* <http://localhost:5000/docs> — Scalar API reference;
* <http://localhost:5000/openapi/v1.json> — OpenAPI document.

This host does not require PostgreSQL.

### 5. Run the PostgreSQL host

```bash
dotnet run --project hosts/FunQuery.PostgreSql.Server
```

```bash
curl "http://localhost:5000/query?source=orders&q=%24source(%40source).%24filter(total%20gt%20100).%24select(id%2C%20customer)"
curl "http://localhost:5000/query?source=people&q=%24source(%40source).%24filter(active).%24count()"
```

`orders` is the PostgreSQL source; `people` is in-memory in the same host.

### 6. Run the tests

```bash
dotnet test
```

With `FUNQUERY_POSTGRES` set, the live PostgreSQL tests run. Without it, they return early; the
in-memory, golden SQL, and unit tests still run. The full suite is 1161 tests with PostgreSQL 18.3.

### 7. Stop and clean up

```powershell
.\scripts\dev-postgres.ps1 stop
.\scripts\dev-postgres.ps1 status
```

```bash
./scripts/dev-postgres.sh stop
./scripts/dev-postgres.sh status
```

To remove the database and its volume manually:

```bash
podman compose -f deploy/compose.yaml down -v
# or
docker compose -f deploy/compose.yaml down -v
```

## Seed schema

`db/seed.sql` creates the table expected by `hosts/FunQuery.PostgreSql.Server`:

| Column       | Type           |
| ------------ | -------------- |
| `id`         | `integer`      |
| `customer`   | `text`         |
| `total`      | `numeric(12,2)`|
| `ordered_on` | `date`         |

The five rows match the in-memory `orders` sample.

## Troubleshooting

| Symptom | Fix |
| ------- | --- |
| Port 5433 already in use | Stop the old container (`podman ps`, `podman stop <name>`) or change the port in `deploy/compose.yaml` and the connection string |
| Container starts but is unhealthy | `podman logs funquery-postgres` or `docker logs funquery-postgres` |
| `orders` table missing | Run the `seed` action; a pre-existing volume is not re-seeded by `start` |
| `psql: error: connection ...` | Confirm the database is running and `FUNQUERY_POSTGRES` uses port `5433` |
| `podman compose` reports a missing provider | Install `podman-compose`, or use Docker Compose; the scripts also detect standalone providers |
| PowerShell blocks the script | Run `powershell -ExecutionPolicy Bypass -File scripts/dev-postgres.ps1 start` |
| Stale data after changing the seed | Run `reset`; it removes the volume and recreates the table |

PostgreSQL 18 changed the image data path: `PGDATA` is version-specific and the compose volume is mounted
at `/var/lib/postgresql`, not `/var/lib/postgresql/data`. Keep that mount if you change the image version.

For serving the generated documentation on a private Tailscale network, see
[`SelfHosting.md`](SelfHosting.md).
