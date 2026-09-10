# IdempotencyKit.StackExchangeRedis

Shared, multi-instance-safe idempotency store for [IdempotencyKit](https://github.com/altayaliev/IdempotencyKit),
backed by Redis. Use this when you run more than one instance of your API and already use (or are
happy to add) Redis.

Targets `net8.0`, `net9.0`, `net10.0`. Depends on `StackExchange.Redis` ≥ 3.1.31, which supports
all three target frameworks with a single version. Works against any Redis (or Redis-protocol
compatible service — KeyDB, Azure Cache for Redis, AWS ElastiCache) with scripting support, i.e.
Redis 2.6+; tested against Redis 7.

## Setup

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitStackExchangeRedisStore(); // optional keyPrefix:, default "idempotencykit:"
```

Already call ASP.NET Core's own `AddStackExchangeRedisCache(...)` for `IDistributedCache`? That
does **not** register an `IConnectionMultiplexer` for this package to use — the
`AddSingleton<IConnectionMultiplexer>(...)` line above is still required, as a separate,
explicit registration (both can safely point at the same Redis server).

No table, migration, or schema needed — a key simply appears in Redis the first time it's
reserved. Every atomicity guarantee comes from a small Lua script Redis runs as one atomic step —
no separate distributed-lock package (RedLock, etc.) is involved.

## Full documentation

See the [project README](https://github.com/altayaliev/IdempotencyKit#readme) for the complete setup
walkthrough, the version compatibility matrix, and how the Lua-script-based atomicity works.
Available in English and Azerbaijani.

---

# IdempotencyKit.StackExchangeRedis

[IdempotencyKit](https://github.com/altayaliev/IdempotencyKit) üçün paylaşılan, çoxinstanslı-təhlükəsiz idempotency
store — Redis üzərində. API-nizin birdən çox instansını işlədirsinizsə və artıq Redis
istifadə edirsinizsə (və ya əlavə etməyə hazırsınızsa) bunu seçin.

`net8.0`, `net9.0`, `net10.0`-ı hədəfləyir. `StackExchange.Redis` ≥ 3.1.31-dən asılıdır, bu da tək
bir versiya ilə hər üç target framework-ü dəstəkləyir. Scripting dəstəkləyən istənilən Redis (və
ya Redis-protokol-uyğun servis — KeyDB, Azure Cache for Redis, AWS ElastiCache) ilə işləyir, yəni
Redis 2.6+; Redis 7 üzərində test olunub.

## Quraşdırma

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddIdempotencyKit();
builder.Services.AddIdempotencyKitStackExchangeRedisStore(); // istəyə bağlı keyPrefix:, default "idempotencykit:"
```

Artıq `IDistributedCache` üçün ASP.NET Core-un öz `AddStackExchangeRedisCache(...)`-ini
çağırırsınız? Bu, bu paketin istifadə edəcəyi `IConnectionMultiplexer`-i **qeydiyyatdan keçirmir**
— yuxarıdakı `AddSingleton<IConnectionMultiplexer>(...)` sətri yenə də ayrıca, açıq şəkildə tələb
olunur (hər ikisi eyni Redis serverinə təhlükəsiz işarə edə bilər).

Heç bir cədvəl, migration və ya sxem lazım deyil — key ilk dəfə rezerv ediləndə sadəcə Redis-də
özü yaranır. Bütün atomiklik zəmanəti Redis-in bir addımda atomic icra etdiyi kiçik bir Lua
skripti ilə təmin olunur — ayrıca distributed-lock paketi (RedLock və s.) heç bir yerdə iştirak etmir.

## Tam sənədləşdirmə

Tam qurulma izahı, versiya uyğunluq matrisi və Lua-script-əsaslı atomikliyin necə işlədiyi üçün
[layihə README-inə](https://github.com/altayaliev/IdempotencyKit#readme) baxın. İngiliscə və
azərbaycanca mövcuddur.
