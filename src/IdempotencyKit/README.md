# IdempotencyKit

Core abstractions for [IdempotencyKit](https://github.com/altayaliev/IdempotencyKit) — zero-config idempotency
for .NET APIs. This package has **no ASP.NET Core dependency**; most users don't install it
directly, since [`IdempotencyKit.AspNetCore`](https://www.nuget.org/packages/IdempotencyKit.AspNetCore) pulls it
in automatically.

Targets `net8.0`, `net9.0`, `net10.0`.

## What's in here

- `IIdempotencyStore` — the storage abstraction every backend (in-memory, Redis, EF Core)
  implements, plus the `IdempotencyRecord` / `IdempotencyReservationResult` types it works with.
- `InMemoryIdempotencyStore` — the default single-instance store.
- `IIdempotencyFingerprintProvider` / `Sha256IdempotencyFingerprintProvider` — tells a legitimate
  replay apart from a key reused for a different request.
- `IdempotencyCleanupService` — a background service that periodically purges expired entries.

## Install this directly only if

You're implementing a custom `IIdempotencyStore` (e.g. for a backend IdempotencyKit doesn't ship a
provider for) without using the ASP.NET Core middleware. For the normal case — an ASP.NET Core
API — install [`IdempotencyKit.AspNetCore`](https://www.nuget.org/packages/IdempotencyKit.AspNetCore) instead,
which brings this package in as a dependency.

## Full documentation

See the [project README](https://github.com/altayaliev/IdempotencyKit#readme) — install/quickstart,
configuration, how the atomicity guarantees work, storage backends (Redis, EF Core), and writing
your own store. Available in English and Azerbaijani.

---

# IdempotencyKit

[IdempotencyKit](https://github.com/altayaliev/IdempotencyKit) üçün core abstraction-lar — .NET API-ləri üçün
zero-config idempotency. Bu paketin **ASP.NET Core asılılığı yoxdur**; əksər istifadəçilər onu
birbaşa quraşdırmır, çünki [`IdempotencyKit.AspNetCore`](https://www.nuget.org/packages/IdempotencyKit.AspNetCore)
onu avtomatik özü ilə gətirir.

`net8.0`, `net9.0`, `net10.0`-ı hədəfləyir.

## Burada nə var

- `IIdempotencyStore` — hər backend-in (in-memory, Redis, EF Core) implementasiya etdiyi saxlama
  abstraksiyası, üstəlik onun işlədiyi `IdempotencyRecord` / `IdempotencyReservationResult` tipləri.
- `InMemoryIdempotencyStore` — default tək-instans store.
- `IIdempotencyFingerprintProvider` / `Sha256IdempotencyFingerprintProvider` — həqiqi replay-i
  fərqli sorğu üçün təkrar istifadə olunan key-dən ayırd edir.
- `IdempotencyCleanupService` — vaxtı keçmiş qeydləri periodik təmizləyən background service.

## Bunu birbaşa yalnız bu halda quraşdırın

ASP.NET Core middleware istifadə etmədən fərdi bir `IIdempotencyStore` yazırsınızsa (məs. IdempotencyKit-nun
provider təklif etmədiyi bir backend üçün). Normal halda — ASP.NET Core API üçün —
[`IdempotencyKit.AspNetCore`](https://www.nuget.org/packages/IdempotencyKit.AspNetCore) quraşdırın, o bu paketi
asılılıq kimi özü ilə gətirir.

## Tam sənədləşdirmə

[Layihə README-inə](https://github.com/altayaliev/IdempotencyKit#readme) baxın — quraşdırma/sürətli
başlanğıc, konfiqurasiya, atomiklik zəmanətlərinin necə işlədiyi, saxlama backend-ləri (Redis, EF
Core) və özünüzünkünü yazmaq. İngiliscə və azərbaycanca mövcuddur.
