# IdempotencyKit

[![CI](https://github.com/altayaliev/IdempotencyKit/actions/workflows/ci.yml/badge.svg)](https://github.com/altayaliev/IdempotencyKit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/IdempotencyKit.svg)](https://www.nuget.org/packages/IdempotencyKit)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Zero-config idempotency for .NET APIs. Register one middleware, and any mutating
request (`POST`/`PUT`/`PATCH`/`DELETE`) carrying an `Idempotency-Key` header is
executed once — retries, double-clicks, and network-triggered resends replay the
original response instead of re-running your handler.

Targets `net8.0`, `net9.0`, and `net10.0`.

---

## English

### Why

A request can reach your server more than once for reasons outside your control:
a client retries after a timeout, a mobile app resends on flaky connectivity, a
user double-clicks "Pay now". Without protection, each arrival can create a
duplicate order, charge a card twice, or otherwise re-run a side effect that was
only meant to happen once.

IdempotencyKit detects the duplicate and returns the first result instead — without you
writing that logic into every endpoint. The goal is that the *technology*
behind it (in-memory, Redis, or a SQL database via EF Core) is an
implementation detail; what your API guarantees — "this operation runs at
most once per key" — does not change as you swap that backend.

### Which package do I need?

| Your situation | Install |
|---|---|
| One instance (dev, a single-replica deployment) | `IdempotencyKit.AspNetCore` only — the bundled in-memory store is enough. |
| Multiple instances, already using Redis | `IdempotencyKit.AspNetCore` + `IdempotencyKit.StackExchangeRedis` |
| Multiple instances, already using a SQL database (any EF Core provider) | `IdempotencyKit.AspNetCore` + `IdempotencyKit.EntityFrameworkCore` |
| Multiple instances, neither Redis nor a SQL database | Pick either — Redis is simpler to add if you have neither; EF Core avoids standing up a new piece of infrastructure if you already run a database. |

`IdempotencyKit` (the core package with no ASP.NET Core dependency) comes in
transitively either way — install it directly only if you're implementing a
custom `IIdempotencyStore` without the ASP.NET Core middleware.

### Version compatibility

IdempotencyKit targets `net8.0`, `net9.0`, and `net10.0`. The table below is the
**actual, tested** compatibility matrix for each provider package's own
dependency — not a guess:

| Package | Depends on | net8.0 app | net9.0 app | net10.0 app |
|---|---|---|---|---|
| `IdempotencyKit`, `IdempotencyKit.AspNetCore` | (none beyond ASP.NET Core itself) | ✅ | ✅ | ✅ |
| `IdempotencyKit.StackExchangeRedis` | `StackExchange.Redis` ≥ 3.1.31 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 8.0.30 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 9.0.19 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 10.0.11 | ❌ | ❌ | ✅ |

Two things worth knowing:

- **`StackExchange.Redis`** is a single package version that genuinely
  supports all three target frameworks — there is nothing to choose here.
- **EF Core is different**: 8.x and 9.x each work across *all three* .NET
  versions (EF Core's own packages are more broadly multi-targeted than
  ASP.NET Core's), but **EF Core 10.x dropped support for net8.0/net9.0
  projects** — a net8.0 or net9.0 app can only use EF Core 8.x or 9.x, never
  10.x. IdempotencyKit's own build references the version shown per its own target
  framework (net8.0 build → EF Core 8.0.30, net9.0 build → 9.0.19, net10.0
  build → 10.0.11) as a **floor**, not a pin — NuGet will happily use a newer
  compatible version already in your project (e.g. EF Core 9.5.0 satisfies the
  8.0.30 floor). This was verified by actually restoring every
  package/target-framework combination above, not inferred from version
  numbers.

Redis server compatibility: any Redis (or Redis-protocol-compatible service —
KeyDB, managed offerings like Azure Cache for Redis or AWS ElastiCache) with
scripting (`EVAL`) support, i.e. **Redis 2.6+**. Tested against Redis 7.

### Install

```bash
dotnet add package IdempotencyKit.AspNetCore
```

This pulls in the `IdempotencyKit` core package as a dependency.

### Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempotencyKit();
var app = builder.Build();

app.UseRouting();
app.UseIdempotencyKit();

app.MapPost("/orders", (OrderRequest request) => /* ... */);

app.Run();
```

That's it — no attribute needed on `/orders`. A client that wants idempotency
sends:

```
POST /orders
Idempotency-Key: 5f2f3a3e-8f5a-4b8e-9f0a-1a2b3c4d5e6f
```

`UseIdempotencyKit()` must be placed **after** `UseRouting()` (or, in a minimal-hosting
app, after any explicit `UseRouting()` call — endpoint metadata such as
`[IdempotencyIgnore]` is only available once routing has resolved the endpoint).

### Request outcomes

| Situation | Result |
|---|---|
| No `Idempotency-Key` header present | Passes through unprotected (unless `RequireHeaderOnMutatingRequests` is enabled — see below) |
| Same key, same request (method + path + body), retried after the first finished | The original response is **replayed**; your handler does **not** run again |
| Same key, same request, while the first is still running | `409 Conflict` — a request with this key is already being processed |
| Same key, but a **different** request (different method, path, or body) | `422 Unprocessable Entity` — the key was reused incorrectly |
| Method not in the configured set (default: `GET`/`HEAD`/etc.) | Passes through untouched — IdempotencyKit only looks at mutating methods |

Opt an endpoint out entirely, regardless of headers, with `[IdempotencyIgnore]`:

```csharp
app.MapPost("/pings", () => "pong").WithMetadata(new IdempotencyIgnoreAttribute());
// or, on an MVC action:
[IdempotencyIgnore]
public IActionResult Ping() => Ok("pong");
```

### Configuration

```csharp
builder.Services.AddIdempotencyKit(options =>
{
    options.HeaderName = "Idempotency-Key";                 // default
    options.Methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "PATCH", "DELETE" };                // default
    options.PendingTimeout = TimeSpan.FromSeconds(60);       // default
    options.CompletedTtl = TimeSpan.FromHours(24);           // default
    options.RequireHeaderOnMutatingRequests = false;         // default
});
```

| Option | Default | Meaning |
|---|---|---|
| `HeaderName` | `"Idempotency-Key"` | The request header IdempotencyKit reads the client-supplied key from. |
| `Methods` | `POST, PUT, PATCH, DELETE` | HTTP methods IdempotencyKit considers. Anything else is ignored entirely. |
| `PendingTimeout` | 60 seconds | How long a reservation is honored while its handler is still running. Should comfortably exceed your slowest expected request. If the owning request crashes or hangs past this, the key becomes available again — protecting against a permanently stuck key, at the cost of a very slow request theoretically racing with a reclaim. Set this above your real p99 latency plus margin. |
| `CompletedTtl` | 24 hours | How long a finished result stays available for replay before the key is fully forgotten and free for reuse. |
| `RequireHeaderOnMutatingRequests` | `false` | When `true`, a mutating request without the header is rejected with `400 Bad Request` instead of passing through unprotected. Turn this on for endpoints where idempotency is not optional (payments, order creation). |
| `MaxKeyLength` | 200 | The longest accepted key; a longer one is rejected with `400` before it ever reaches the store. Without this, an attacker (or a buggy client) could send unbounded numbers of huge, always-unique keys and inflate the store until the next cleanup pass — matches the column length `IdempotencyKit.EntityFrameworkCore`'s default model uses for the key. |
| `MaxRequestBodyBytes` | 1 MiB | The largest request body the middleware will buffer to fingerprint and cache for replay. A larger request passes through **without** idempotency protection instead of buffering an unbounded body into memory — keep large-upload endpoints off `Methods`, or opt them out with `[IdempotencyIgnore]`, rather than relying on this alone. |

Misconfiguration (e.g. `CompletedTtl` shorter than `PendingTimeout`, an empty
`HeaderName`) is caught at **startup**, not as confusing runtime behavior —
`AddIdempotencyKit()` registers validation that runs via ASP.NET Core's
`ValidateOnStart()`.

`AddIdempotencyKit()` also registers a background `IdempotencyCleanupService` that
periodically purges expired entries — configure its interval as a second
argument:

```csharp
builder.Services.AddIdempotencyKit(
    configure: options => { /* ... */ },
    configureCleanup: cleanup => cleanup.Interval = TimeSpan.FromMinutes(5)); // default
```

Without this, a key nobody ever retries would otherwise sit in the backend
forever — `TryReserveAsync` only reclaims an expired entry lazily, the next
time that *exact* key happens to be looked up again.

A third delegate configures ASP.NET Core-specific hooks that need an
`HttpContext` (kept out of `IdempotencyOptions`, which has no ASP.NET Core
dependency) — currently just `KeyResolver`, for scoping keys, e.g. in a
multi-tenant app where the same client-supplied key from two different
tenants must never be treated as the same operation:

```csharp
builder.Services.AddIdempotencyKit(configureHttp: http =>
    http.KeyResolver = (context, rawKey) => $"{context.User.FindFirst("tenant_id")?.Value}:{rawKey}");
```

### Per-endpoint overrides

Every option above applies app-wide by default. A single endpoint (or, on a
controller, every one of its actions) can override `PendingTimeout`,
`CompletedTtl`, `RequireHeaderOnMutatingRequests`, and `MaxRequestBodyBytes`
without touching the rest of the app — useful when one endpoint (e.g.
payments) needs stricter or longer-lived behavior than the rest:

```csharp
// Minimal API
app.MapPost("/payments", Handler)
    .WithIdempotencyOptions(
        requireHeaderOnMutatingRequests: true,
        completedTtl: TimeSpan.FromDays(7));

// MVC controller action
[IdempotencyOptions(RequireHeaderOnMutatingRequests = true, CompletedTtlSeconds = 604_800)]
public IActionResult CreatePayment(...) { ... }
```

Any property left unset falls back to the app-wide `IdempotencyOptions`.

### How it works

All the atomicity guarantees live behind one method on `IIdempotencyStore`:

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyReservationResult> TryReserveAsync(
        string key, string fingerprintHash, TimeSpan pendingTimeout, TimeSpan completedTtl,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default);

    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
```

`TryReserveAsync` is a state machine (`Pending` → `Completed`) backed by whatever
atomic primitive the store's backend offers natively — an in-memory
compare-and-set, a Redis `SET NX`, a SQL unique constraint — **no external
distributed-lock library required**. It returns one of:

- **`Reserved`** — no prior record (or an abandoned one past `PendingTimeout`);
  the caller now owns the key and must execute the operation.
- **`InProgress`** — a request with the same key **and** fingerprint is
  currently being handled elsewhere → `409`.
- **`Completed`** — a request with the same key and fingerprint already
  finished → its stored response is replayed.
- **`Conflict`** — the key exists but with a **different** fingerprint → `422`.

The **fingerprint** (`IIdempotencyFingerprintProvider`, SHA-256 by default) is
computed over `{method}\n{path+query string}\n{body}`. This is what lets
IdempotencyKit tell a legitimate replay apart from a key accidentally reused for a
different request — it never trusts the header value alone, and a request
that differs only by a query parameter (`?discount=10` vs `?discount=20`) is
correctly treated as a different request, not a replay.

If your handler throws, the middleware calls `ReleaseAsync` before letting the
exception propagate, so a legitimate retry with the same key is not blocked
until `PendingTimeout` elapses.

If the handler itself **succeeds** but persisting that result afterward fails
(a transient store outage), the caller still gets the real, successful
response — IdempotencyKit does not turn a working operation into a false `500` just
because caching its result failed. That failure is logged at `Error` and
counted on the `idempotencykit.complete_failures` metric (see
[Observability](#observability)) precisely because it's the one case IdempotencyKit
can't fully protect against: the reservation stays `Pending`, so a retry
within `PendingTimeout` still correctly gets `409`, but a retry arriving
*after* `PendingTimeout` will re-run the handler, since the result was never
durably recorded. This is a fundamental limit of middleware-level idempotency
in general, not something a library can fully paper over — the metric exists
so it's an observable, alertable event rather than a silent one.

Response compression (`app.UseResponseCompression()`) works correctly with
IdempotencyKit out of the box, verified by an integration test: place
`UseResponseCompression()` before `UseIdempotencyKit()` (the normal recommended
position for it, ahead of `UseRouting()` too) and both the original and every
replayed response compress correctly.

`PurgeExpiredAsync` — called periodically by `IdempotencyCleanupService`, see
[Configuration](#configuration) — removes entries `TryReserveAsync`'s own lazy
reclaim would never reach (a key nobody retries). A backend with native
per-entry expiry (Redis's own `PX`) can implement it as a no-op.

Every type IdempotencyKit itself serializes (`IdempotencyRecord`, the header
dictionary) goes through a source-generated `System.Text.Json` context rather
than reflection — see `IdempotencyKitJsonContext` — so the library stays trim- and
Native AOT-friendly.

### Observability

IdempotencyKit emits standard `System.Diagnostics.Metrics`/`ActivitySource` telemetry
— no OpenTelemetry package dependency is added by IdempotencyKit itself; wire it into
whatever OTel (or other listener) setup you already have:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("IdempotencyKit"))
    .WithTracing(t => t.AddSource("IdempotencyKit"));
```

- **Meter `"IdempotencyKit"`**: `idempotencykit.reservations` (a counter, tagged
  `idempotencykit.outcome` = `reserved`/`in_progress`/`completed`/`conflict` — this is
  how you answer "how many 409s/422s are we actually seeing in production")
  and `idempotencykit.cleanup.purged` (expired entries removed per cleanup pass).
- **ActivitySource `"IdempotencyKit"`**: one `idempotencykit.reserve` span per request that
  reaches the store, tagged with the key and outcome.
- **Structured logging**: `IdempotencyMiddleware` and `IdempotencyCleanupService`
  log through the standard `ILogger` — a `Conflict` (key reused for a
  different request) logs at `Warning`, since it usually signals a client bug.

### Storage backends

The bundled `InMemoryIdempotencyStore` (a `ConcurrentDictionary` under the
hood, in the core `IdempotencyKit` package) works out of the box for a single instance
— local development, tests, or a single-replica deployment. **It does not
share state across processes or machines**: if you run more than one instance
behind a load balancer, each instance has its own view of reservations, and
the guarantee only holds per-instance.

For multi-instance deployments, swap in one of the shared stores below —
`AddIdempotencyKit()` still sets up the middleware, options, and fingerprint provider;
these packages only replace the `IIdempotencyStore` itself.

#### Redis — `IdempotencyKit.StackExchangeRedis`

```bash
dotnet add package IdempotencyKit.StackExchangeRedis
```

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitStackExchangeRedisStore(); // optional keyPrefix: parameter, default "idempotencykit:"
```

Requires an `IConnectionMultiplexer` to already be registered — register it
yourself as above (`using StackExchange.Redis;`). `ConnectionMultiplexer.Connect`
is the synchronous overload shown here so the snippet drops into any
`Program.cs` unmodified; `ConnectionMultiplexer.ConnectAsync(...)` works the
same way if you're already `await`-ing other startup steps.

> **Common mistake:** calling ASP.NET Core's own
> `builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379")`
> (for `IDistributedCache`) does **not** register an `IConnectionMultiplexer`
> for other code to resolve — it manages its own internal connection. If you
> already use `AddStackExchangeRedisCache`, you still need the
> `AddSingleton<IConnectionMultiplexer>(...)` line above as a separate,
> explicit registration (they can safely point at the same Redis server).

Every atomicity guarantee is provided by a small Lua script Redis runs as one
atomic step (`TryReserve.lua` — see `LuaScripts.cs`) — no separate
distributed-lock package (RedLock, etc.) is involved. No table, migration, or
schema is needed — a key just appears in Redis the first time it's reserved.

If other data already lives in the same Redis instance/database, the default
`keyPrefix` (`"idempotencykit:"`) keeps IdempotencyKit's keys from colliding with it; change
it only if `"idempotencykit:"` itself might collide with something else.

#### EF Core — `IdempotencyKit.EntityFrameworkCore`

```bash
dotnet add package IdempotencyKit.EntityFrameworkCore
```

`IdempotencyKit.EntityFrameworkCore` does **not** bundle a database driver — install
whichever EF Core provider package you already use for your own entities
(exactly one of these, matching your database):

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL   # PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.SqlServer  # SQL Server
dotnet add package Microsoft.EntityFrameworkCore.Sqlite     # SQLite
dotnet add package Pomelo.EntityFrameworkCore.MySql         # MySQL / MariaDB
```

Step 1 — register `AppDbContext` as a **factory**, not with the usual
`AddDbContext`:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitEntityFrameworkCoreStore<AppDbContext>();
```

> **Common mistake:** if you already call `builder.Services.AddDbContext<AppDbContext>(...)`
> elsewhere, that registration alone is **not enough** —
> `EfCoreIdempotencyStore<TContext>` specifically needs `IDbContextFactory<TContext>`,
> because `IIdempotencyStore` is a singleton and a normal scoped `DbContext`
> is not safe to capture at that lifetime. Add `AddDbContextFactory<AppDbContext>(...)`
> alongside your existing `AddDbContext` call if you use `AppDbContext` for
> your own entities too — both can target the same `AppDbContext` class and
> the same database without conflict.

Step 2 — add the table to your existing model, in your existing `AppDbContext`
(this is one more table in your database, not a separate database):

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ... your own DbSet<T> properties ...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ... your own entity configuration ...
        modelBuilder.ConfigureIdempotencyStore(); // adds the IdempotencyRecords table
    }
}
```

Step 3 — **create the table**. `ConfigureIdempotencyStore()` only updates EF
Core's *model*; like any other entity you add, the physical table still needs
a migration:

```bash
dotnet ef migrations add AddIdempotencyRecords
dotnet ef database update
```

(For a throwaway/test database only, `context.Database.EnsureCreated()` /
`EnsureCreatedAsync()` also works and skips migrations entirely — do not use
this in production alongside real migrations, they don't mix.)

Works with any relational EF Core provider (SQL Server, PostgreSQL, SQLite,
MySQL via Pomelo, ...) — see [Version compatibility](#version-compatibility)
for which EF Core version line each target framework supports. Atomicity
comes from the table's primary key (`Key`) for new reservations, and from EF
Core's optimistic concurrency check for reclaiming an abandoned one — no raw
SQL required.

`PurgeExpiredAsync` reads and deletes expired rows in pages of 500 rather
than loading the entire expired backlog into memory at once — one round trip
per page in the common case, falling back to a slower row-by-row delete only
for a page where a row was concurrently reclaimed by `TryReserveAsync` since
it was read.

#### Writing your own

Implement `IIdempotencyStore` against any backend with an atomic "insert if
absent" primitive for `TryReserveAsync`, then register it — either before
`AddIdempotencyKit()`, or after with
`services.AddSingleton<IIdempotencyStore, YourStore>()` (`AddIdempotencyKit()` only
fills in the store if none is already registered).

### Testing this repository

```bash
dotnet build IdempotencyKit.slnx     # builds net8.0, net9.0, and net10.0
dotnet test IdempotencyKit.slnx      # runs on all three target frameworks
```

The Redis provider's tests need a real Redis instance reachable at
`localhost:16379` (override with the `IDEMPOTENCYKIT_TEST_REDIS` environment
variable):

```bash
docker run -d --name idempotencykit-test-redis -p 16379:6379 redis:7-alpine
```

Every `IIdempotencyStore` implementation — in-memory, EF Core, Redis, and any
future one — is held to the **same behavioral contract**:
`IdempotencyKit.TestKit`'s `IdempotencyStoreContractTests` is an abstract xUnit base
class exercising concurrent-reservation race safety (many parallel callers →
exactly one winner), replay of a completed record, fingerprint-mismatch
conflict, release-then-retry, releasing after completion being a safe no-op,
reclaiming an abandoned pending reservation past `PendingTimeout`,
completed-TTL expiry, key isolation, and `PurgeExpiredAsync` never disturbing
an active reservation. Each store's test project supplies only a
`CreateStore(TimeProvider)` factory; the rest runs unmodified. Each store's
own test project additionally verifies `PurgeExpiredAsync` actually removes
truly-expired entries and reports the right count (a no-op for Redis, whose
keys expire natively).

Coverage as of this release:

- **`IdempotencyKit.Tests`**: the shared contract suite against
  `InMemoryIdempotencyStore`, plus `Sha256IdempotencyFingerprintProvider`
  tests (determinism, collision-resistance across method/path/body, output
  format, empty-body handling).
- **`IdempotencyKit.EntityFrameworkCore.Tests`**: the shared contract suite against
  `EfCoreIdempotencyStore` running on a real SQLite database (a shared-cache
  in-memory database per test, so unique-constraint violations and EF Core's
  SQL translation are genuinely exercised, not simulated), plus a
  response-headers-through-JSON round-trip test and a
  separate-`DbContext`-instances test.
- **`IdempotencyKit.StackExchangeRedis.Tests`**: the shared contract suite against
  `RedisIdempotencyStore` running against a **real Redis server** (so the Lua
  scripts' actual atomicity is exercised, not just their logic on paper),
  plus a response-round-trip test and a separate-connection test modeling two
  application instances sharing one Redis.
- **`IdempotencyKit.AspNetCore.Tests`** (via `WebApplicationFactory` against a real HTTP
  pipeline): replay without re-running the handler, concurrent duplicate requests
  executing the handler exactly once, conflicting reuse returning `422`,
  `[IdempotencyIgnore]` bypassing the middleware, requests without a key passing
  through, a handler exception releasing the key for retry, `400` when
  `RequireHeaderOnMutatingRequests` is set and the header is missing, and a
  custom `HeaderName` being honored.
- All of the above run — not just compile — against `net8.0`, `net9.0`, and
  `net10.0` individually.
- `samples/IdempotencyKit.Samples.MinimalApi` was manually exercised end-to-end over
  real HTTP (normal request, retried replay, conflicting-body reuse, and 8
  genuinely concurrent duplicate requests producing exactly one order).

### Performance under load

A manual load run (not part of the automated suite — it needs a running
server, so it's a one-off measurement rather than a CI check) against the
Release-mode sample, `InMemoryIdempotencyStore`, single machine:

- **20,000 requests, each a distinct key, 400 concurrent** → ~12,500 req/s,
  0 errors, p50/p95/p99 latency 27 / 63 / 173 ms.
- **15,000 requests collapsing onto 300 distinct keys (50 truly concurrent
  duplicates per key)** → exactly 300 orders created (not 15,000), every
  replayed response byte-identical, no unexpected status code — the
  correctness guarantee holds under real concurrent load, not just in the
  50-caller unit test.

Take this as a directional data point, not a formal benchmark: single
machine, in-memory store only (Redis/EF Core add their own network/DB
round-trip latency on top), and not repeated across hardware or hosting
environments. A repeatable, automated benchmark suite (ideally per backend,
in CI) is still on the [Roadmap](#roadmap).

### Project layout

```
src/IdempotencyKit                          core abstractions + in-memory store (no ASP.NET Core dependency)
src/IdempotencyKit.AspNetCore               middleware + DI extensions
src/IdempotencyKit.EntityFrameworkCore      shared EF Core store
src/IdempotencyKit.StackExchangeRedis       shared Redis store
tests/IdempotencyKit.TestKit                the shared IIdempotencyStore behavioral contract suite
tests/IdempotencyKit.Tests                  unit tests for the in-memory store and fingerprint provider
tests/IdempotencyKit.AspNetCore.Tests       integration tests for the middleware
tests/IdempotencyKit.EntityFrameworkCore.Tests   contract + provider-specific tests (real SQLite)
tests/IdempotencyKit.StackExchangeRedis.Tests    contract + provider-specific tests (real Redis)
samples/IdempotencyKit.Samples.MinimalApi   a runnable example
```

### Roadmap

- A repeatable, automated benchmark suite per backend (see [Performance under load](#performance-under-load) for an initial manual measurement)

### License

MIT — see [LICENSE](LICENSE).

---

## Azərbaycanca

### Niyə

Sorğu sizin nəzarətinizdən kənar səbəblərdən serverə birdən çox dəfə çata bilər:
müştəri timeout-dan sonra təkrar cəhd edir, mobil tətbiq qeyri-sabit
bağlantıda sorğunu yenidən göndərir, istifadəçi "Ödə" düyməsini iki dəfə basır.
Qorunma olmadan, hər çatma ayrıca sifariş yarada, kartdan iki dəfə pul çıxara,
və ya yalnız bir dəfə baş verməli olan digər yan-effekti təkrar icra edə bilər.

IdempotencyKit dublikatı aşkar edib ilk nəticəni qaytarır — bunun üçün hər endpoint-də
ayrıca məntiq yazmağa ehtiyac qalmır. Məqsəd budur ki, arxasındakı *texnologiya*
(yaddaş, Redis, və ya EF Core vasitəsilə SQL verilənlər bazası) implementasiya
detalı olsun; API-nizin verdiyi zəmanət — "bu əməliyyat hər açar üçün ən çoxu
bir dəfə icra olunur" — backend dəyişsə də dəyişməsin.

### Hansı paket mənə lazımdır?

| Vəziyyətiniz | Quraşdırın |
|---|---|
| Tək instans (development, tək-repli deployment) | Yalnız `IdempotencyKit.AspNetCore` — daxil olunan in-memory store kifayətdir. |
| Çoxlu instans, artıq Redis istifadə edirsiniz | `IdempotencyKit.AspNetCore` + `IdempotencyKit.StackExchangeRedis` |
| Çoxlu instans, artıq SQL verilənlər bazası (istənilən EF Core provider) istifadə edirsiniz | `IdempotencyKit.AspNetCore` + `IdempotencyKit.EntityFrameworkCore` |
| Çoxlu instans, nə Redis, nə SQL verilənlər bazası | İkisindən birini seçin — heç biri yoxdursa Redis əlavə etmək daha sadədir; artıq DB işlədirsinizsə EF Core yeni infrastruktur qurmağın qarşısını alır. |

`IdempotencyKit` (ASP.NET Core asılılığı olmayan core paket) hər iki halda dolayı
yolla (transitively) gəlir — onu birbaşa yalnız middleware-siz, öz
`IIdempotencyStore`-unuzu yazırsınızsa quraşdırın.

### Versiya uyğunluğu

IdempotencyKit `net8.0`, `net9.0` və `net10.0`-ı hədəfləyir. Aşağıdakı cədvəl hər
provider paketinin öz asılılığı üçün **real, test edilmiş** uyğunluq
matrisidir — təxmin deyil:

| Paket | Asılı olduğu | net8.0 tətbiq | net9.0 tətbiq | net10.0 tətbiq |
|---|---|---|---|---|
| `IdempotencyKit`, `IdempotencyKit.AspNetCore` | (ASP.NET Core-dan başqa heç nə) | ✅ | ✅ | ✅ |
| `IdempotencyKit.StackExchangeRedis` | `StackExchange.Redis` ≥ 3.1.31 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 8.0.30 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 9.0.19 | ✅ | ✅ | ✅ |
| `IdempotencyKit.EntityFrameworkCore` | EF Core ≥ 10.0.11 | ❌ | ❌ | ✅ |

Bilməli olduğunuz iki şey:

- **`StackExchange.Redis`** tək bir paket versiyasıdır və həqiqətən hər üç
  target framework-ü dəstəkləyir — burada seçim yoxdur.
- **EF Core fərqlidir**: 8.x və 9.x hər ikisi *bütün üç* .NET versiyasında
  işləyir (EF Core-un öz paketləri ASP.NET Core-dan daha geniş multi-target
  edilib), amma **EF Core 10.x net8.0/net9.0 layihələri üçün dəstəyi
  dayandırıb** — net8.0 və ya net9.0 tətbiqi yalnız EF Core 8.x və ya 9.x
  istifadə edə bilər, heç vaxt 10.x yox. IdempotencyKit-nun öz build-i öz target
  framework-ünə uyğun versiyaya istinad edir (net8.0 build → EF Core 8.0.30,
  net9.0 build → 9.0.19, net10.0 build → 10.0.11) **minimum (floor)** kimi,
  sabitlənmiş (pin) versiya kimi yox — NuGet layihənizdə artıq olan daha
  yüksək uyğun versiyanı məmnuniyyətlə istifadə edir (məs. EF Core 9.5.0,
  8.0.30 minimumunu ödəyir). Bu, versiya nömrələrindən çıxarım deyil,
  yuxarıdakı hər paket/target-framework kombinasiyasını həqiqətən restore
  edərək təsdiqlənib.

Redis server uyğunluğu: scripting (`EVAL`) dəstəkləyən istənilən Redis (və ya
Redis-protokol-uyğun servis — KeyDB, Azure Cache for Redis, AWS ElastiCache
kimi idarəolunan xidmətlər), yəni **Redis 2.6+**. Redis 7 üzərində test
olunub.

### Quraşdırma

```bash
dotnet add package IdempotencyKit.AspNetCore
```

Bu, `IdempotencyKit` core paketini asılılıq kimi özü ilə gətirir.

### Sürətli başlanğıc

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempotencyKit();
var app = builder.Build();

app.UseRouting();
app.UseIdempotencyKit();

app.MapPost("/orders", (OrderRequest request) => /* ... */);

app.Run();
```

Bu qədər — `/orders`-də heç bir atribut lazım deyil. İdempotentlik istəyən
müştəri belə göndərir:

```
POST /orders
Idempotency-Key: 5f2f3a3e-8f5a-4b8e-9f0a-1a2b3c4d5e6f
```

`UseIdempotencyKit()` mütləq `UseRouting()`-dən **sonra** yerləşdirilməlidir (minimal
hosting tətbiqində də, əgər `UseRouting()` açıq şəkildə çağırılıbsa, ondan
sonra) — çünki `[IdempotencyIgnore]` kimi endpoint metadata-sı yalnız routing
endpoint-i həll etdikdən sonra mövcud olur.

### Sorğunun nəticələri

| Vəziyyət | Nəticə |
|---|---|
| `Idempotency-Key` header-i yoxdur | Qorunmadan keçir (əgər `RequireHeaderOnMutatingRequests` aktiv deyilsə — aşağıya bax) |
| Eyni key, eyni sorğu (metod + path + body), ilk bitdikdən sonra təkrarlanıb | Orijinal cavab **replay** olunur; handler-iniz **yenidən işləmir** |
| Eyni key, eyni sorğu, ilk hələ icra olunarkən | `409 Conflict` — bu key ilə sorğu artıq emal olunur |
| Eyni key, amma **fərqli** sorğu (fərqli metod, path və ya body) | `422 Unprocessable Entity` — key səhv təkrar istifadə olunub |
| Metod konfiqurasiya olunmuş dəstdə deyil (default: `GET`/`HEAD` və s.) | Toxunulmadan keçir — IdempotencyKit yalnız dəyişdirici metodlara baxır |

Header-dən asılı olmayaraq bir endpoint-i tamamilə istisna etmək üçün
`[IdempotencyIgnore]`:

```csharp
app.MapPost("/pings", () => "pong").WithMetadata(new IdempotencyIgnoreAttribute());
// və ya MVC action üzərində:
[IdempotencyIgnore]
public IActionResult Ping() => Ok("pong");
```

### Konfiqurasiya

```csharp
builder.Services.AddIdempotencyKit(options =>
{
    options.HeaderName = "Idempotency-Key";                 // default
    options.Methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "PATCH", "DELETE" };                // default
    options.PendingTimeout = TimeSpan.FromSeconds(60);       // default
    options.CompletedTtl = TimeSpan.FromHours(24);           // default
    options.RequireHeaderOnMutatingRequests = false;         // default
});
```

| Seçim | Default | Mənası |
|---|---|---|
| `HeaderName` | `"Idempotency-Key"` | IdempotencyKit-nun müştəri tərəfindən verilən açarı oxuduğu header. |
| `Methods` | `POST, PUT, PATCH, DELETE` | IdempotencyKit-nun nəzərə aldığı HTTP metodları. Digərləri tamamilə nəzərə alınmır. |
| `PendingTimeout` | 60 saniyə | Handler hələ icra olunarkən rezervasiyanın etibarlı sayıldığı müddət. Gözlənilən ən yavaş sorğunuzdan rahat şəkildə çox olmalıdır. Sahib sorğu çökərsə və ya bu müddəti keçərsə, key yenidən sərbəst olur — bu, key-in əbədi "yapışıb qalmasının" qarşısını alır, əvəzində nəzəri olaraq çox yavaş bir sorğu bərpa ilə üst-üstə düşə bilər. Bunu real p99 latency-nizdən yuxarı, ehtiyat payı ilə seçin. |
| `CompletedTtl` | 24 saat | Bitmiş nəticənin replay üçün nə qədər müddət əlçatan qalacağı — bundan sonra key tamamilə unudulur və yenidən istifadəyə açıq olur. |
| `RequireHeaderOnMutatingRequests` | `false` | `true` olduqda, header olmadan gələn dəyişdirici sorğu qorunmadan keçmək əvəzinə `400 Bad Request` ilə rədd edilir. İdempotentliyin məcburi olduğu endpoint-lər üçün (ödənişlər, sifariş yaratma) bunu aktivləşdirin. |
| `MaxKeyLength` | 200 | Qəbul edilən ən uzun key — daha uzunu store-a çatmadan `400` ilə rədd edilir. Bu olmasa, kimsə (və ya səhv client) minlərlə, həmişə unikal, çox uzun key göndərib növbəti cleanup keçidinə qədər store-u doldura bilər — `IdempotencyKit.EntityFrameworkCore`-un default modelinin key sütunu uzunluğu ilə üst-üstə düşür. |
| `MaxRequestBodyBytes` | 1 MiB | Middleware-in fingerprint hesablamaq və replay üçün yaddaşa yığacağı ən böyük request body. Bundan böyük sorğu idempotentlik qorunması **olmadan** keçir — böyük body-li unbounded yaddaşa yığılmır. Böyük fayl-yükləmə endpoint-lərini `Methods`-dan çıxarın, ya da `[IdempotencyIgnore]` ilə istisna edin, təkcə bu limitə güvənməyin. |

Səhv konfiqurasiya (məs. `PendingTimeout`-dan qısa `CompletedTtl`, boş
`HeaderName`) **startup**-da tutulur, qarışıq runtime davranışı kimi yox —
`AddIdempotencyKit()` ASP.NET Core-un `ValidateOnStart()` mexanizmi ilə işləyən
validasiya qeydiyyatdan keçirir.

`AddIdempotencyKit()` həmçinin vaxtı keçmiş qeydləri periodik təmizləyən background
`IdempotencyCleanupService`-i qeydiyyatdan keçirir — intervalını ikinci
arqumentlə konfiqurasiya edin:

```csharp
builder.Services.AddIdempotencyKit(
    configure: options => { /* ... */ },
    configureCleanup: cleanup => cleanup.Interval = TimeSpan.FromMinutes(5)); // default
```

Bu olmasa, heç kimin təkrar sorğu göndərmədiyi bir key backend-də əbədi
qalardı — `TryReserveAsync` vaxtı keçmiş qeydi yalnız *məhz həmin* key yenidən
axtarılanda lazy şəkildə bərpa edir.

Üçüncü delegate `HttpContext` tələb edən ASP.NET Core-a xas hook-ları
konfiqurasiya edir (bunlar `IdempotencyOptions`-dan kənar saxlanılır, çünki o,
ASP.NET Core asılılığı daşımır) — hazırda yalnız `KeyResolver`, key-ləri
scope etmək üçün, məs. multi-tenant tətbiqdə eyni client-verilmiş key iki
fərqli tenant-dan gəlsə eyni əməliyyat sayılmamalıdır:

```csharp
builder.Services.AddIdempotencyKit(configureHttp: http =>
    http.KeyResolver = (context, rawKey) => $"{context.User.FindFirst("tenant_id")?.Value}:{rawKey}");
```

### Endpoint üzrə override-lər

Yuxarıdakı bütün opsiyalar defolt olaraq bütün tətbiq üçün keçərlidir. Tək bir
endpoint (və ya bir controller-də bütün action-lar) `PendingTimeout`,
`CompletedTtl`, `RequireHeaderOnMutatingRequests` və `MaxRequestBodyBytes`
dəyərlərini qalan tətbiqə toxunmadan override edə bilər — məsələn, ödəniş
endpoint-i digərlərindən daha uzun ömürlü və ya daha ciddi davranış tələb
edəndə:

```csharp
// Minimal API
app.MapPost("/payments", Handler)
    .WithIdempotencyOptions(
        requireHeaderOnMutatingRequests: true,
        completedTtl: TimeSpan.FromDays(7));

// MVC controller action
[IdempotencyOptions(RequireHeaderOnMutatingRequests = true, CompletedTtlSeconds = 604_800)]
public IActionResult CreatePayment(...) { ... }
```

Boş buraxılan hər property özünü tətbiq-üzrə `IdempotencyOptions`-a geri
qaytarır.

### Necə işləyir

Bütün atomiklik zəmanətləri `IIdempotencyStore` üzərindəki bir metodun
arxasındadır:

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyReservationResult> TryReserveAsync(
        string key, string fingerprintHash, TimeSpan pendingTimeout, TimeSpan completedTtl,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default);

    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
```

`TryReserveAsync` — store-un backend-inin təbii olaraq təqdim etdiyi atomic
əməliyyata (yaddaşda compare-and-set, Redis `SET NX`, SQL unique constraint)
əsaslanan bir state machine-dir (`Pending` → `Completed`) — **xarici
distributed-lock kitabxanası tələb olunmur**. Aşağıdakılardan birini qaytarır:

- **`Reserved`** — əvvəlki qeyd yoxdur (və ya `PendingTimeout`-u keçmiş
  tərk edilmiş qeyddir); çağıran indi key-in sahibidir və əməliyyatı icra
  etməlidir.
- **`InProgress`** — eyni key **və** fingerprint ilə sorğu hazırda başqa yerdə
  icra olunur → `409`.
- **`Completed`** — eyni key və fingerprint ilə sorğu artıq bitib → saxlanmış
  cavab replay olunur.
- **`Conflict`** — key mövcuddur, amma **fərqli** fingerprint ilə → `422`.

**Fingerprint** (`IIdempotencyFingerprintProvider`, default SHA-256)
`{method}\n{path+query string}\n{body}` üzərində hesablanır. Bu, IdempotencyKit-ya
həqiqi replay-i key-in səhvən fərqli sorğu üçün təkrar istifadə
olunmasından ayırd etməyə imkan verir — heç vaxt yalnız header dəyərinə
güvənmir, və yalnız query parametri ilə fərqlənən sorğu (`?discount=10` vs
`?discount=20`) düzgün şəkildə fərqli sorğu sayılır, replay yox.

Handler-iniz istisna atarsa, middleware istisnanı buraxmadan əvvəl
`ReleaseAsync` çağırır — beləliklə eyni key ilə edilən legitim təkrar cəhd
`PendingTimeout` keçənə qədər bloklanmır.

Handler-in özü **uğurlu olsa**, amma nəticəni sonradan saxlamaq uğursuz olsa
(store-un keçici kəsilməsi), çağıran yenə də real, uğurlu cavabı alır —
IdempotencyKit işləyən əməliyyatı, sadəcə nəticəni cache-ləmək uğursuz oldu deyə,
yalançı `500`-ə çevirmir. Bu, `Error` səviyyəsində log olunur və
`idempotencykit.complete_failures` metrikasında sayılır (bax:
[Müşahidəedilənlik](#müşahidəedilənlik-observability)) — məhz çünki bu, IdempotencyKit-nun
tam qoruya bilmədiyi tək haldır: rezervasiya `Pending` olaraq qalır, ona görə
`PendingTimeout` daxilində retry düzgün `409` alır, amma `PendingTimeout`-dan
**sonra** gələn retry handler-i yenidən icra edəcək, çünki nəticə heç vaxt
davamlı şəkildə qeydə alınmayıb. Bu, ümumiyyətlə middleware-səviyyəli
idempotentliyin fundamental məhdudiyyətidir, kitabxananın tam örtə biləcəyi
bir şey deyil — metrika elə buna görə var ki, bu, sükutlu deyil, müşahidə
edilə bilən/alert qoyula bilən hadisə olsun.

Response compression (`app.UseResponseCompression()`) IdempotencyKit ilə qutudan
çıxan kimi düzgün işləyir, integration test ilə təsdiqlənib:
`UseResponseCompression()`-u `UseIdempotencyKit()`-dan əvvəl yerləşdirin (onun normal
tövsiyə olunan mövqeyi, `UseRouting()`-dən də əvvəl) — həm orijinal, həm də
hər replay edilən cavab düzgün sıxılır.

`PurgeExpiredAsync` — `IdempotencyCleanupService` tərəfindən periodik
çağırılır (bax: [Konfiqurasiya](#konfiqurasiya)) — `TryReserveAsync`-in öz lazy
bərpasının heç vaxt toxunmayacağı qeydləri (heç kimin təkrarlamadığı key)
silir. Native per-entry expiry-si olan backend (Redis-in öz `PX`-i) bunu
no-op kimi implementasiya edə bilər.

IdempotencyKit-nun özünün serializasiya etdiyi hər tip (`IdempotencyRecord`, header
dictionary-si) reflection əvəzinə source-generated `System.Text.Json`
context-dən keçir — bax: `IdempotencyKitJsonContext` — bu, kitabxananı trim- və
Native AOT-uyğun saxlayır.

### Müşahidəedilənlik (Observability)

IdempotencyKit standart `System.Diagnostics.Metrics`/`ActivitySource` telemetriyasını
yayımlayır — IdempotencyKit özü heç bir OpenTelemetry paket asılılığı əlavə etmir;
artıq sahib olduğunuz OTel (və ya başqa listener) qurulumuna qoşun:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("IdempotencyKit"))
    .WithTracing(t => t.AddSource("IdempotencyKit"));
```

- **Meter `"IdempotencyKit"`**: `idempotencykit.reservations` (counter, `idempotencykit.outcome` teqi
  ilə = `reserved`/`in_progress`/`completed`/`conflict` — bu, "production-da
  faktiki neçə 409/422 görürük" sualına cavab verməyin yoludur) və
  `idempotencykit.cleanup.purged` (hər cleanup keçidində silinən vaxtı keçmiş qeydlər).
- **ActivitySource `"IdempotencyKit"`**: store-a çatan hər sorğu üçün key və outcome
  teqli bir `idempotencykit.reserve` span-ı.
- **Strukturlaşdırılmış logging**: `IdempotencyMiddleware` və
  `IdempotencyCleanupService` standart `ILogger` vasitəsilə log yazır —
  `Conflict` (key fərqli sorğu üçün təkrar istifadə olunub) `Warning`
  səviyyəsində log olunur, çünki bu adətən client-in səhvini göstərir.

### Saxlama backend-ləri

Daxil olunan `InMemoryIdempotencyStore` (arxasında `ConcurrentDictionary`, core
`IdempotencyKit` paketinin daxilində) tək instans üçün — lokal development, testlər
və ya tək-repli deployment üçün — qutudan çıxan kimi işləyir. **Proseslər/
maşınlar arasında state paylaşmır**: load balancer arxasında birdən çox
instans işlədirsinizsə, hər instansın öz rezervasiya görünüşü olur və zəmanət
yalnız instans-daxili qüvvədədir.

Çoxinstanslı deployment üçün aşağıdakı paylaşılan store-lardan birini
qoşun — `AddIdempotencyKit()` yenə middleware, options və fingerprint provider-i
qurur; bu paketlər yalnız `IIdempotencyStore`-un özünü əvəz edir.

#### Redis — `IdempotencyKit.StackExchangeRedis`

```bash
dotnet add package IdempotencyKit.StackExchangeRedis
```

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitStackExchangeRedisStore(); // istəyə bağlı keyPrefix: parametri, default "idempotencykit:"
```

`IConnectionMultiplexer`-in artıq qeydiyyatdan keçmiş olması tələb olunur —
onu yuxarıdakı kimi özünüz qeydiyyatdan keçirin (`using StackExchange.Redis;`).
`ConnectionMultiplexer.Connect` sinxron overload-dır, ona görə bu snippet
istənilən `Program.cs`-ə dəyişiklik etmədən yerləşir; artıq başqa start-up
addımlarında `await` edirsinizsə, `ConnectionMultiplexer.ConnectAsync(...)`
də eyni şəkildə işləyir.

> **Tez-tez rast gəlinən səhv:** ASP.NET Core-un öz
> `builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379")`
> çağırışı (`IDistributedCache` üçün) `IConnectionMultiplexer`-i **qeydiyyatdan
> keçirmir** — o öz daxili bağlantısını idarə edir. Artıq
> `AddStackExchangeRedisCache` istifadə edirsinizsə, yenə də yuxarıdakı
> `AddSingleton<IConnectionMultiplexer>(...)` sətrini ayrıca, açıq şəkildə
> əlavə etməlisiniz (hər ikisi eyni Redis serverinə təhlükəsiz işarə edə bilər).

Bütün atomiklik zəmanəti Redis-in bir addımda atomic icra etdiyi kiçik bir
Lua skripti ilə təmin olunur (`TryReserve.lua` — bax: `LuaScripts.cs`) —
ayrıca distributed-lock paketi (RedLock və s.) heç bir yerdə iştirak etmir.
Heç bir cədvəl, migration və ya sxem lazım deyil — key ilk dəfə rezerv
ediləndə sadəcə Redis-də özü yaranır.

Eyni Redis instansında/database-də başqa data da varsa, default `keyPrefix`
(`"idempotencykit:"`) IdempotencyKit-nun key-lərinin onunla toqquşmasının qarşısını alır —
yalnız `"idempotencykit:"`-un özü nə isə ilə toqquşarsa dəyişdirin.

#### EF Core — `IdempotencyKit.EntityFrameworkCore`

```bash
dotnet add package IdempotencyKit.EntityFrameworkCore
```

`IdempotencyKit.EntityFrameworkCore` verilənlər bazası driver-i özü ilə **gətirmir** —
öz entity-ləriniz üçün artıq istifadə etdiyiniz EF Core provider paketini
quraşdırın (bazanıza uyğun, bunlardan dəqiq birini):

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL   # PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.SqlServer  # SQL Server
dotnet add package Microsoft.EntityFrameworkCore.Sqlite     # SQLite
dotnet add package Pomelo.EntityFrameworkCore.MySql         # MySQL / MariaDB
```

Addım 1 — `AppDbContext`-i adi `AddDbContext` ilə yox, **factory** kimi
qeydiyyatdan keçirin:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitEntityFrameworkCoreStore<AppDbContext>();
```

> **Tez-tez rast gəlinən səhv:** artıq başqa yerdə
> `builder.Services.AddDbContext<AppDbContext>(...)` çağırırsınızsa, bu
> qeydiyyat tək başına **kifayət deyil** — `EfCoreIdempotencyStore<TContext>`
> məhz `IDbContextFactory<TContext>` tələb edir, çünki `IIdempotencyStore`
> singleton-dır və adi scoped `DbContext`-i bu ömür müddətində "tutmaq"
> təhlükəsiz deyil. Öz entity-ləriniz üçün də `AppDbContext`-i istifadə
> edirsinizsə, mövcud `AddDbContext` çağırışınızın yanına
> `AddDbContextFactory<AppDbContext>(...)` əlavə edin — hər ikisi eyni
> `AppDbContext` sinfinə və eyni verilənlər bazasına ziddiyyətsiz işarə edə bilər.

Addım 2 — cədvəli mövcud modelinizə, mövcud `AppDbContext`-inizə əlavə edin
(bu, verilənlər bazanızda bir cədvəl daha deməkdir, ayrıca baza demək deyil):

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ... öz DbSet<T> propertiləriniz ...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ... öz entity konfiqurasiyanız ...
        modelBuilder.ConfigureIdempotencyStore(); // IdempotencyRecords cədvəlini əlavə edir
    }
}
```

Addım 3 — **cədvəli yaradın**. `ConfigureIdempotencyStore()` yalnız EF
Core-un *modelini* yeniləyir; əlavə etdiyiniz hər hansı digər entity kimi,
fiziki cədvəl üçün yenə də migration lazımdır:

```bash
dotnet ef migrations add AddIdempotencyRecords
dotnet ef database update
```

(Yalnız birdəfəlik/test verilənlər bazası üçün, `context.Database.EnsureCreated()`
/ `EnsureCreatedAsync()` də işləyir və migration-ları tamamilə keçir — bunu
real migration-ların yanında production-da istifadə etməyin, ikisi bir yerdə
işləmir.)

Hər hansı relational EF Core provider-i ilə işləyir (SQL Server, PostgreSQL,
SQLite, MySQL Pomelo vasitəsilə və s.) — hansı EF Core versiya xəttinin hansı
target framework-ü dəstəklədiyi üçün bax: [Versiya uyğunluğu](#versiya-uyğunluğu).
Atomiklik yeni rezervasiyalar üçün cədvəlin primary key-indən (`Key`), tərk
edilmiş rezervasiyanı bərpa etmək üçün isə EF Core-un optimistic concurrency
yoxlamasından gəlir — heç bir xam SQL lazım deyil.

`PurgeExpiredAsync` bütün müddəti bitmiş sətirləri bir dəfəyə yaddaşa yükləmək
əvəzinə 500-lük səhifələrlə oxuyub silir — adi halda hər səhifə üçün tək bir
round-trip, yalnız bir sətir `TryReserveAsync` tərəfindən oxunduqdan sonra
paralel olaraq bərpa edilibsə həmin səhifə üçün sətir-sətir silməyə keçir.

#### Özünüzünkünü yazmaq

`TryReserveAsync` üçün atomic "yoxdursa əlavə et" prinsipi olan istənilən
backend-ə qarşı `IIdempotencyStore`-u implementasiya edin, sonra qeydiyyatdan
keçirin — ya `AddIdempotencyKit()`-dan əvvəl, ya da sonra
`services.AddSingleton<IIdempotencyStore, YourStore>()` ilə (`AddIdempotencyKit()`
yalnız heç bir store qeydiyyatdan keçməyibsə özününkini əlavə edir).

### Bu repo-nun testləri

```bash
dotnet build IdempotencyKit.slnx     # net8.0, net9.0 və net10.0 üçün build edir
dotnet test IdempotencyKit.slnx      # hər üç target framework üzərində işə salır
```

Redis provider-inin testləri `localhost:16379`-da real bir Redis instansı
tələb edir (`IDEMPOTENCYKIT_TEST_REDIS` mühit dəyişəni ilə override edilə bilər):

```bash
docker run -d --name idempotencykit-test-redis -p 16379:6379 redis:7-alpine
```

Hər bir `IIdempotencyStore` implementasiyası — in-memory, EF Core, Redis və
gələcək hər hansı biri — **eyni davranış müqaviləsinə** tabedir:
`IdempotencyKit.TestKit` paketindəki `IdempotencyStoreContractTests` — paralel
rezervasiya race-təhlükəsizliyi (çoxlu paralel çağıran → dəqiq bir qalib),
bitmiş qeydin replay-i, fingerprint uyğunsuzluğu conflict-i, release-sonra-retry,
completed olduqdan sonra release-in təhlükəsiz no-op olması, `PendingTimeout`-u
keçmiş tərk edilmiş rezervasiyanın bərpası, completed-TTL bitməsi, key
izolyasiyası, və `PurgeExpiredAsync`-in aktiv rezervasiyaya heç vaxt
toxunmamasını yoxlayan mücərrəd xUnit bazasıdır. Hər store-un test layihəsi
yalnız bir `CreateStore(TimeProvider)` fabrikası verir — qalanı olduğu kimi
işə düşür. Hər store-un öz test layihəsi əlavə olaraq `PurgeExpiredAsync`-in
həqiqətən vaxtı keçmiş qeydləri silib düzgün sayı qaytardığını da yoxlayır
(Redis üçün no-op-dur, çünki onun key-ləri native şəkildə bitir).

Bu buraxılışa olan əhatə:

- **`IdempotencyKit.Tests`**: `InMemoryIdempotencyStore` üzərində ortaq contract dəsti,
  üstəlik `Sha256IdempotencyFingerprintProvider` testləri (determinizm,
  metod/path/body üzrə toqquşmaya davamlılıq, format, boş body).
- **`IdempotencyKit.EntityFrameworkCore.Tests`**: `EfCoreIdempotencyStore` üzərində
  ortaq contract dəsti, real SQLite üzərində (hər test üçün shared-cache
  in-memory DB — unique-constraint pozuntuları və EF Core-un SQL tərcüməsi
  simulyasiya deyil, həqiqətən yoxlanılır), üstəlik header-lərin JSON
  vasitəsilə round-trip testi və ayrı-ayrı `DbContext` instansları testi.
- **`IdempotencyKit.StackExchangeRedis.Tests`**: `RedisIdempotencyStore` üzərində
  ortaq contract dəsti, **real Redis server** üzərində (Lua skriptlərinin
  həqiqi atomikliyi yoxlanılır, kağız üzərindəki məntiq deyil), üstəlik cavab
  round-trip testi və iki tətbiq instansının bir Redis-i paylaşmasını
  modelləşdirən ayrı-bağlantı testi.
- **`IdempotencyKit.AspNetCore.Tests`** (`WebApplicationFactory` ilə real HTTP pipeline
  üzərində): handler yenidən işləmədən replay, paralel dublikat sorğuların
  handler-i dəqiq bir dəfə icra etməsi, ziddiyyətli təkrar-istifadənin `422`
  qaytarması, `[IdempotencyIgnore]`-un middleware-i keçib getməsi, key-siz
  sorğuların sərbəst keçməsi, handler istisnasının key-i retry üçün sərbəst
  buraxması, `RequireHeaderOnMutatingRequests` aktiv olub header əskik olanda
  `400`, və fərdi `HeaderName`-in nəzərə alınması.
- Yuxarıdakıların hamısı `net8.0`, `net9.0` və `net10.0` üzərində ayrı-ayrı
  **icra edilib** — sadəcə compile yoxlanılmayıb.
- `samples/IdempotencyKit.Samples.MinimalApi` real HTTP üzərindən əl ilə uçdan-uca
  yoxlanılıb (normal sorğu, retry-replay, ziddiyyətli body ilə təkrar istifadə,
  və həqiqətən paralel 8 dublikat sorğunun dəqiq bir sifariş yaratması).

### Yük altında performans

Əl ilə aparılmış bir load test (avtomatik test dəstinin hissəsi deyil — işə
salınmış server tələb etdiyi üçün CI yoxlaması deyil, birdəfəlik ölçmədir),
Release rejimindəki sample-a qarşı, `InMemoryIdempotencyStore`, tək maşın:

- **20,000 sorğu, hər biri fərqli key, 400 paralel** → ~12,500 req/s, 0 xəta,
  p50/p95/p99 latency 27 / 63 / 173 ms.
- **300 fərqli key üzərinə düşən 15,000 sorğu (hər key üçün 50 həqiqətən
  paralel dublikat)** → dəqiq 300 sifariş yaradıldı (15,000 yox), hər replay
  edilmiş cavab bayt-bayta eyni, gözlənilməz status kodu yox — korrektlik
  zəmanəti real paralel yük altında da qüvvədədir, təkcə 50-çağıranlı unit
  testdə yox.

Bunu rəsmi benchmark kimi yox, istiqamətverici məlumat nöqtəsi kimi qəbul
edin: tək maşın, yalnız in-memory store (Redis/EF Core öz şəbəkə/DB
round-trip latency-sini üstünə əlavə edir), müxtəlif hardware/hosting
mühitlərində təkrarlanmayıb. Təkrarlanan, avtomatlaşdırılmış benchmark dəsti
(hər backend üçün, ideal halda CI-də) hələ [Yol xəritəsi](#yol-xəritəsi)ndədir.

### Layihə strukturu

```
src/IdempotencyKit                          core abstraction + in-memory store (ASP.NET Core asılılığı yoxdur)
src/IdempotencyKit.AspNetCore               middleware + DI extension-lar
src/IdempotencyKit.EntityFrameworkCore      paylaşılan EF Core store
src/IdempotencyKit.StackExchangeRedis       paylaşılan Redis store
tests/IdempotencyKit.TestKit                ortaq IIdempotencyStore davranış müqaviləsi dəsti
tests/IdempotencyKit.Tests                  in-memory store və fingerprint provider üçün unit testlər
tests/IdempotencyKit.AspNetCore.Tests       middleware üçün integration testlər
tests/IdempotencyKit.EntityFrameworkCore.Tests   contract + provider-ə xas testlər (real SQLite)
tests/IdempotencyKit.StackExchangeRedis.Tests    contract + provider-ə xas testlər (real Redis)
samples/IdempotencyKit.Samples.MinimalApi   işə salına bilən nümunə
```

### Yol xəritəsi

- Hər backend üçün təkrarlanan, avtomatlaşdırılmış benchmark dəsti (bax: [Yük altında performans](#yük-altında-performans) — ilkin əl ilə ölçmə üçün)

### Lisenziya

MIT — bax: [LICENSE](LICENSE).
