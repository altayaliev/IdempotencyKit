# IdempotencyKit.AspNetCore

Zero-config idempotency middleware for ASP.NET Core, from
[IdempotencyKit](https://github.com/altayaliev/IdempotencyKit). Register one middleware, and any mutating request
(`POST`/`PUT`/`PATCH`/`DELETE`) carrying an `Idempotency-Key` header is executed once — retries,
double-clicks, and network-triggered resends replay the original response instead of re-running
your handler. No attribute needed on the endpoint.

Targets `net8.0`, `net9.0`, `net10.0`.

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempotencyKit();
var app = builder.Build();

app.UseRouting();
app.UseIdempotencyKit();

app.MapPost("/orders", (OrderRequest request) => /* ... */);

app.Run();
```

A client that wants idempotency sends:

```
POST /orders
Idempotency-Key: 5f2f3a3e-8f5a-4b8e-9f0a-1a2b3c4d5e6f
```

- Same key, same request, retried → the original response is **replayed**.
- Same key, same request, still running → `409 Conflict`.
- Same key, a **different** request → `422 Unprocessable Entity`.
- No key present → passes through unprotected (configurable — see below).

Opt an endpoint out with `[IdempotencyIgnore]`.

## Multi-instance deployments

The bundled in-memory store only works for a single instance. For more than one instance behind
a load balancer, add [`IdempotencyKit.StackExchangeRedis`](https://www.nuget.org/packages/IdempotencyKit.StackExchangeRedis)
or [`IdempotencyKit.EntityFrameworkCore`](https://www.nuget.org/packages/IdempotencyKit.EntityFrameworkCore) —
see the full README linked below for setup of either.

## Full documentation

See the [project README](https://github.com/altayaliev/IdempotencyKit#readme) for the complete request-
outcome table, every `IdempotencyOptions` setting, how the atomicity guarantees work, the version
compatibility matrix for each storage provider, and troubleshooting notes. Available in English
and Azerbaijani.

---

# IdempotencyKit.AspNetCore

[IdempotencyKit](https://github.com/altayaliev/IdempotencyKit)-dan ASP.NET Core üçün zero-config idempotency
middleware-i. Bir middleware qeydiyyatdan keçirin, `Idempotency-Key` header-i daşıyan hər
dəyişdirici sorğu (`POST`/`PUT`/`PATCH`/`DELETE`) bir dəfə icra olunur — retry-lar, iki dəfə
klikləmə və şəbəkə-təkrarları handler-inizi yenidən işlətmək əvəzinə orijinal cavabı replay edir.
Endpoint-də heç bir atribut lazım deyil.

`net8.0`, `net9.0`, `net10.0`-ı hədəfləyir.

## Sürətli başlanğıc

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempotencyKit();
var app = builder.Build();

app.UseRouting();
app.UseIdempotencyKit();

app.MapPost("/orders", (OrderRequest request) => /* ... */);

app.Run();
```

İdempotentlik istəyən müştəri belə göndərir:

```
POST /orders
Idempotency-Key: 5f2f3a3e-8f5a-4b8e-9f0a-1a2b3c4d5e6f
```

- Eyni key, eyni sorğu, təkrarlanıb → orijinal cavab **replay** olunur.
- Eyni key, eyni sorğu, hələ icra olunur → `409 Conflict`.
- Eyni key, **fərqli** sorğu → `422 Unprocessable Entity`.
- Key yoxdur → qorunmadan keçir (konfiqurasiya edilə bilir — aşağıya bax).

Bir endpoint-i `[IdempotencyIgnore]` ilə istisna edin.

## Çoxinstanslı deployment-lər

Daxil olunan in-memory store yalnız tək instans üçün işləyir. Load balancer arxasında birdən çox
instans üçün [`IdempotencyKit.StackExchangeRedis`](https://www.nuget.org/packages/IdempotencyKit.StackExchangeRedis)
və ya [`IdempotencyKit.EntityFrameworkCore`](https://www.nuget.org/packages/IdempotencyKit.EntityFrameworkCore)
əlavə edin — hər ikisinin qurulması üçün aşağıdakı tam README-yə baxın.

## Tam sənədləşdirmə

Tam sorğu-nəticə cədvəli, hər bir `IdempotencyOptions` seçimi, atomiklik zəmanətlərinin necə
işlədiyi, hər saxlama provider-i üçün versiya uyğunluq matrisi və troubleshooting qeydləri üçün
[layihə README-inə](https://github.com/altayaliev/IdempotencyKit#readme) baxın. İngiliscə və
azərbaycanca mövcuddur.
