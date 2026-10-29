# StockWise — Architecture & Key Decisions

## 1. Overview

StockWise is a **modular monolith**: one deployable ASP.NET Core API backed by
PostgreSQL, with a React SPA frontend. The modules have strict dependency directions:

```
StockWise.Api  →  StockWise.Application  →  StockWise.Domain
                          ↑
StockWise.Infrastructure ─┘
```

- **StockWise.Domain** — entities, enums, domain exceptions. No dependencies.
- **StockWise.Application** — use-case services, DTOs, FluentValidation validators,
  abstractions (`IAppDbContext`, `IPermissionChecker`, `ITokenService`, …).
- **StockWise.Infrastructure** — EF Core (`StockWiseDbContext`), Npgsql, migrations,
  password hashing (BCrypt), JWT + refresh-token services, dev email outbox, seeding.
- **StockWise.Api** — controllers, authentication, permission-based authorization,
  rate limiting, ProblemDetails error mapping, Swagger (dev only), health checks.
- **stockwise-web** — React 18 (JavaScript, not TypeScript) + Vite + Tailwind CSS,
  TanStack Query for server state, React Hook Form, Recharts, Lucide icons.

## 2. Tenancy & security

### Tenant isolation (defense in depth)

1. **Active organization is derived from the JWT** (`tid` claim, minted only after a
   membership check). The client cannot choose a tenant id; switching organizations
   calls `POST /api/v1/auth/switch-organization`, which re-validates membership and
   re-issues tokens.
2. **Every service query filters explicitly by `TenantId`** — this is the primary boundary.
3. **Global EF Core query filters** add a second layer: every `TenantEntity` gets
   `e.TenantId == DbContext.CurrentTenantId`, where the context property reads a
   scoped `ITenantContext` that the auth middleware populates from the JWT.
   EF evaluates context-rooted member access per query execution, so one compiled
   model serves all tenants.
4. **Integration tests prove isolation**: a user from Tenant A gets 404s (not leaks)
   when reading or mutating Tenant B's products/stock.

### Authentication

- Short-lived **JWT access tokens (15 min)** held in memory by the SPA — never in
  `localStorage`.
- **Refresh-token rotation** in an `HttpOnly`, `SameSite=Lax` cookie scoped to
  `/api/v1/auth` (7 days). Every refresh revokes the used token and links its
  replacement, so replay is detectable. Logout revokes server-side.
- Passwords: BCrypt (work factor 11). Account lockout after 5 failed logins (15 min).
  Login responses are uniform regardless of account existence.
- Password reset: single-use hashed tokens (1 hour); all sessions are revoked on reset.
- Email verification and invitations use hashed opaque tokens. **Development email
  mode** writes messages to the `email_messages` table and logs them instead of
  sending; a production provider plugs into the same `IEmailSender` interface.

### Authorization

- Permission catalogue (`Products.Read`, `PurchaseOrders.Approve`, …) mapped to
  per-tenant roles (`RolePermission`). Six system role templates are seeded per
  organization (Business Owner, Business Admin, Inventory Manager, Procurement
  Officer, Approver, Storekeeper, Sales/Read-Only).
- Server-side enforcement: controllers declare `[AuthorizePermission(Permissions.X)]`,
  backed by a dynamic authorization policy provider + `IPermissionChecker`
  (30-second cached membership lookup). The frontend only mirrors permissions for
  menus/buttons — hiding UI is never the security boundary.

### Other security measures

- Rate limiting (fixed window per IP): auth endpoints 20/min, write endpoints
  120/min, global 600/min (configurable).
- All errors return RFC-style ProblemDetails; unexpected exceptions are logged and
  return a generic message (no internals leak).
- CORS restricted to configured origins. No secrets in logs. Inputs validated with
  FluentValidation + safe maximum page sizes everywhere.
- CSRF: the browser never holds an auth token usable cross-site for state changes —
  the refresh cookie is scoped to the auth path and only mints tokens via a POST;
  API calls require the in-memory `Authorization` header.

## 3. Inventory is a ledger

`InventoryMovement` is the **append-only source of truth**. `InventoryBalance` is a
concurrency-protected per (product, warehouse) projection — recalculable from the
ledger at any time (an integration test reconciles the two).

Every stock change goes through **`InventoryPostingService.PostAsync`**, which in one
ambient transaction:

1. Validates product status, warehouse, unit fractionality and tenant ownership.
2. Creates/updates `Batch` rows for batch-tracked products (batch number + expiry
   required; minimum remaining shelf life enforced, with the policy "a batch is
   valid through its expiry date and expired from the next day, in the
   organization's time zone").
3. Locks the balance row (`SELECT … FOR UPDATE`; `INSERT … ON CONFLICT DO NOTHING`
   first so first-touch races cannot duplicate rows) — concurrent postings to the
   same balance serialize, so stock can never drift.
4. Maintains weighted-average cost on inbound movements and carries the average on
   outbound movements.
5. Enforces the **negative-stock policy** (rejected unless the organization opted in).
6. Writes the immutable `InventoryMovement` row.

`inventory_movements` additionally carries a **database trigger** (created in the
migration) that rejects any UPDATE or DELETE — corrections are compensating
movements, enforced at the database level.

FEFO (First-Expired-First-Out) allocation is used when removing stock from a
batch-tracked product without an explicit batch. Batches can be quarantined and
released by permission (`Batches.Manage`).

## 4. Procurement

`Draft → PendingApproval → Approved → Ordered → PartiallyReceived → Received`,
plus `Rejected` / `Cancelled`. Transitions are validated server-side.

- Configurable **approval threshold**: orders at/below the threshold auto-approve on
  submit; otherwise an `Approver` must act.
- **Separation of duties** (configurable): the creator of an order cannot approve it.
- Goods receipts: only against `Approved` / `Ordered` / `PartiallyReceived` orders;
  per-line outstanding checks; **over-receipt** rejected unless enabled with a
  tolerance percent; batch/expiry/shelf-life rules apply.
- **Idempotency**: receipts, adjustments, opening balances and transfers accept an
  idempotency key; the first execution stores a response snapshot
  (`IdempotencyRecord`) and retries return the original result — a network retry can
  never receive stock twice. Receipt posting additionally takes a `FOR UPDATE` lock
  on the purchase order to serialize concurrent receiving.

## 5. Data model highlights

- GUID public identifiers; `TenantId` + composite indexes on all tenant-owned tables.
- Unique indexes: tenant+SKU, tenant+barcode (where set), tenant+supplier code,
  tenant+warehouse code, tenant+PO/GRN/transfer/count numbers, batch
  (tenant+product+warehouse+batch number), refresh-token and invitation token hashes.
- Money/quantities use `decimal(18,4)`; timestamps are stored UTC and converted to the
  organization's IANA time zone for date bucketing (expiry, dashboard trends).
- Numbering (`PO-000123`) comes from a per-tenant `NumberSequence` row updated under
  a row lock inside the posting transaction.
- Documented deferred items: no payment/billing integration (plan assignment is
  admin-managed metadata), supplier price-history comparison, purchase-request
  (requisition) step, warehouse-level user access restrictions, PDF rendering
  (print-friendly HTML via the browser instead).

## 6. Testing

- **Unit** (17): expiry classification policy, permission catalogue integrity,
  BCrypt hashing round-trip/salting, JWT claims and lifetime.
- **Integration** (18): run against a disposable PostgreSQL via Testcontainers
  through the real HTTP pipeline — auth + lockout + uniform errors, cross-tenant
  isolation, opening balances, ledger/balance reconciliation, idempotent receiving,
  concurrent adjustment correctness, negative-stock policy, batch/shelf-life rules,
  PO lifecycle with separation of duties, over-receipt policy, transfer transitions,
  and single-post stock counts.

## 7. Known limitations (MVP scope)

- Sales/revenue are not captured: "top-moving products" use movement volume, and no
  gross-profit metrics are claimed anywhere.
- Reports aggregate over the balance projection and ledger; a scheduled
  reconciliation job is a natural next step (the ledger remains the source of truth).
- Email sending is dev-mode only until a provider adapter is configured.
- Single warehouse selector is a UI filter; permission-by-warehouse is not yet
  enforced server-side (`WarehouseUserAccess` entity reserved in the spec).
- The frontend test layer (Vitest/RTL/Playwright) is not yet in place; CI builds and
  packages the frontend and runs the full backend suite.
