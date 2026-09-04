# Idempo

Zero-config idempotency for .NET APIs. Register one middleware, and any mutating
request (`POST`/`PUT`/`PATCH`/`DELETE`) carrying an `Idempotency-Key` header is
executed once — retries, double-clicks, and network-triggered resends replay the
original response instead of re-running your handler.

Targets `net8.0`, `net9.0`, and `net10.0`.

## Why

A request can reach your server more than once for reasons outside your control:
a client retries after a timeout, a mobile app resends on flaky connectivity, a
user double-clicks "Pay now". Without protection, each arrival can create a
duplicate order, charge a card twice, or otherwise re-run a side effect that was
only meant to happen once. Idempo detects the duplicate and returns the first
result instead — without you writing that logic into every endpoint.

## Install

```bash
dotnet add package Idempo.AspNetCore
```

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempo();
var app = builder.Build();

app.UseRouting();
app.UseIdempo();

app.MapPost("/orders", (OrderRequest request) => /* ... */);

app.Run();
```

That's it — no attribute needed on `/orders`. A client that wants idempotency
sends:

```
POST /orders
Idempotency-Key: 5f2f3a3e-8f5a-4b8e-9f0a-1a2b3c4d5e6f
```

- **Same key, same request, retried** → the original response is replayed; the
  handler does not run again.
- **Same key, two requests in flight at once** → the second gets `409 Conflict`
  while the first is still being processed.
- **Same key, different request** (different method/path/body) → `422
  Unprocessable Entity`, since the key was reused incorrectly.
- **No key present** → the request passes through unprotected (or, if
  `RequireHeaderOnMutatingRequests` is enabled, is rejected with `400`).

Opt an endpoint out entirely with `[IdempotencyIgnore]`.

## How it works

`IIdempotencyStore` exposes one atomic operation, `TryReserveAsync`, that every
backend implements using whatever atomic primitive it has natively (an in-memory
compare-and-set, a Redis `SET NX`, a SQL unique constraint) — no external
distributed-lock library required. It returns one of:

- `Reserved` — no prior record; the caller executes the operation.
- `InProgress` — the same key+fingerprint is currently being handled.
- `Completed` — the same key+fingerprint already finished; replay its result.
- `Conflict` — the key was reused for a different request.

The in-process default (`InMemoryIdempotencyStore`) works out of the box for
single-instance apps and testing. A shared backend (Redis, EF Core) is needed
once you run more than one instance — see `IIdempotencyStore` to implement your
own.

## Project layout

- `src/Idempo` — core abstractions and the in-memory store (no ASP.NET Core dependency).
- `src/Idempo.AspNetCore` — the middleware and DI extensions.
- `tests/` — unit tests for the store's concurrency semantics, and integration tests for the middleware.
- `samples/Idempo.Samples.MinimalApi` — a runnable example.

## License

MIT
