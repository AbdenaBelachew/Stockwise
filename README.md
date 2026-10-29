# StockWise

**Know what is selling, what is expiring, and what needs reordering — before losing money.**

StockWise is a multi-tenant inventory and procurement SaaS for retailers, pharmacies,
distributors, and small manufacturers. It treats inventory as an auditable ledger of
movements (not an editable quantity field), enforces tenant isolation and granular
permissions on the server, and supports batch/expiry (FEFO) tracking.

---

## Quick start (local development)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22+](https://nodejs.org) and npm
- [Docker](https://www.docker.com) (for PostgreSQL via docker compose)

### 1. Start PostgreSQL

```bash
docker compose up -d db
```

The compose file maps PostgreSQL to **host port 5433** so it does not clash with a
locally installed PostgreSQL (5432).

### 2. Run the API

```bash
cd src/StockWise.Api
dotnet run
```

- API:      http://localhost:5105
- Swagger:  http://localhost:5105/swagger (development only)
- Health:   http://localhost:5105/health and /health/ready
- In Development the API applies EF migrations automatically on startup.

### 3. Run the web app

```bash
cd stockwise-web
npm install
npm run dev
```

- Web app: http://localhost:5173 (Vite proxies `/api` to the API on :5105, so
  authentication cookies are same-origin).

### 4. Seed demo data (explicit, development-only)

```bash
cd src/StockWise.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed-demo
```

This creates a demo organization with products, stock (via real ledger movements),
a purchase order awaiting approval, and five users — all with the password
`DemoPassw0rd!`:

| Email                             | Role               |
| --------------------------------- | ------------------ |
| `admin@demo.stockwise.local`      | Business Owner     |
| `manager@demo.stockwise.local`    | Inventory Manager  |
| `buyer@demo.stockwise.local`      | Procurement Officer|
| `store@demo.stockwise.local`      | Storekeeper        |
| `approver@demo.stockwise.local`   | Approver           |

> **Never** run `seed-demo` against a production database. There is no default
> administrator password anywhere in the product; the first real account is created
> through the normal sign-up flow.

### Run the tests

```bash
dotnet test tests/StockWise.Tests
```

- Unit tests run in-memory.
- Integration tests spin up a disposable PostgreSQL via **Testcontainers** (Docker
  required) and exercise the real API: authentication, tenant isolation, the stock
  ledger, idempotent receiving, concurrent adjustments, transfers, and stock counts.

## Docker Compose (full stack)

```bash
docker compose up --build
```

| Service | URL                     |
| ------- | ----------------------- |
| Web     | http://localhost:8080   |
| API     | http://localhost:5105   |
| DB      | localhost:5433          |

Then seed demo data once (from the repo root):

```bash
docker compose exec api dotnet StockWise.Api.dll seed-demo
```

## Configuration

| Setting (env form)              | Purpose                                                        |
| ------------------------------- | -------------------------------------------------------------- |
| `ConnectionStrings__Default`    | PostgreSQL connection string.                                  |
| `Jwt__Key`                      | JWT signing key, **min 32 chars**. Required in production.     |
| `Jwt__Issuer` / `Jwt__Audience` | Token issuer/audience (defaults `stockwise` / `stockwise-web`).|
| `Cors__AllowedOrigins__0`       | Frontend origin(s) allowed for CORS.                           |
| `Database__ApplyMigrationsOnStartup` | Auto-apply migrations (development only — use `dotnet ef database update` in production). |
| `RateLimiting__AuthPermitLimit` | Auth endpoint rate limit per minute per IP (default 20).       |

`.env.example` files exist at the repo root and in `src/StockWise.Api/` and
`stockwise-web/`. **Never commit real secrets** — supply them via environment
variables or a secret manager.

## Database notes

The `AddFilteringIndexes` migration creates composite indexes for every list/filter
path (status, warehouse, movement type, dates) plus **pg_trgm GIN indexes** that make
`LIKE '%term%'` product/supplier/order searches use an index. `pg_trgm` is a
trusted extension: the migration can create it when the connecting role owns the
database (the docker compose user does). On managed PostgreSQL, a DBA runs
`CREATE EXTENSION IF NOT EXISTS pg_trgm;` once, then migrations apply normally.

## Production checklist

- HTTPS everywhere; set the HSTS header at your reverse proxy. The refresh-token
  cookie is `Secure` outside Development.
- Override `Jwt__Key` with a strong random value (≥ 32 chars).
- Provision PostgreSQL with strong credentials; enable backups and test the restore
  procedure regularly (`pg_dump stockwise > backup.sql` / `pg_restore`).
- Run migrations explicitly: `dotnet ef database update --project src/StockWise.Infrastructure --startup-project src/StockWise.Api`.
- Restrict CORS to the real frontend origin.
- Point logging at a persistent sink (Serilog sinks) — no secrets are logged by design.
- Health endpoints: `GET /health` (liveness) and `GET /health/ready` (includes a DB check).

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the module layout, domain model,
business rules, security decisions, and known limitations.

## Repository layout

```
src/
  StockWise.Domain/          Entities, enums, exceptions (no dependencies)
  StockWise.Application/     Use cases, DTOs, validation, business workflows
  StockWise.Infrastructure/  EF Core, PostgreSQL, identity, email, seeding
  StockWise.Api/             HTTP endpoints, auth, middleware, Swagger
tests/
  StockWise.Tests/           Unit + integration tests (Testcontainers)
stockwise-web/               React (Vite, JavaScript) + Tailwind frontend
docker-compose.yml           PostgreSQL + API + web
docs/ARCHITECTURE.md         Architecture and decisions
```
