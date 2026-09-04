# Idempo.EntityFrameworkCore

Shared, multi-instance-safe idempotency store for [Idempo](https://github.com/altayaliev/Idempo),
backed by any relational EF Core provider (SQL Server, PostgreSQL, SQLite, MySQL via Pomelo, ...).
Use this when you run more than one instance of your API and already use a SQL database.

Targets `net8.0`, `net9.0`, `net10.0`. **Version note:** EF Core 8.x and 9.x both work across all
three target frameworks, but EF Core 10.x only supports net10.0 projects — see the
[version compatibility matrix](https://github.com/altayaliev/Idempo#version-compatibility) for the
full picture before you pick versions.

## Setup

This package does **not** bundle a database driver — install your own EF Core provider first
(exactly one, matching your database): `Npgsql.EntityFrameworkCore.PostgreSQL`,
`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Sqlite`, or
`Pomelo.EntityFrameworkCore.MySql`.

```csharp
// 1. Register your DbContext as a FACTORY (not the usual AddDbContext) —
//    EfCoreIdempotencyStore is a singleton and needs IDbContextFactory<TContext>.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddIdempo();
builder.Services.AddIdempoEntityFrameworkCoreStore<AppDbContext>();
```

```csharp
// 2. Add the table to your existing model — one more table, not a separate database.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ConfigureIdempotencyStore();
}
```

```bash
# 3. Create the table — ConfigureIdempotencyStore() only updates the EF Core *model*.
dotnet ef migrations add AddIdempotencyRecords
dotnet ef database update
```

Already call `AddDbContext<AppDbContext>(...)` elsewhere for your own entities? That registration
alone isn't enough for this package — add `AddDbContextFactory<AppDbContext>(...)` alongside it;
both can safely target the same `AppDbContext` class and database.

## Full documentation

See the [project README](https://github.com/altayaliev/Idempo#readme) for the complete setup
walkthrough (with the common-mistakes callouts above explained in full), the version compatibility
matrix, and how `IIdempotencyStore`'s atomicity guarantee is implemented here (the table's primary
key plus EF Core's optimistic concurrency check — no raw SQL). Available in English and
Azerbaijani.

---

# Idempo.EntityFrameworkCore

[Idempo](https://github.com/altayaliev/Idempo) üçün paylaşılan, çoxinstanslı-təhlükəsiz idempotency
store — istənilən relational EF Core provider-i ilə işləyir (SQL Server, PostgreSQL, SQLite,
Pomelo vasitəsilə MySQL və s.). API-nizin birdən çox instansını işlədirsinizsə və artıq SQL
verilənlər bazası istifadə edirsinizsə bunu seçin.

`net8.0`, `net9.0`, `net10.0`-ı hədəfləyir. **Versiya qeydi:** EF Core 8.x və 9.x hər üç target
framework-də işləyir, amma EF Core 10.x yalnız net10.0 layihələrini dəstəkləyir — versiya
seçməzdən əvvəl [versiya uyğunluq matrisinə](https://github.com/altayaliev/Idempo#version-compatibility)
baxın.

## Quraşdırma

Bu paket verilənlər bazası driver-i özü ilə **gətirmir** — öz EF Core provider-inizi əvvəlcə
quraşdırın (bazanıza uyğun, dəqiq birini): `Npgsql.EntityFrameworkCore.PostgreSQL`,
`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Sqlite`, və ya
`Pomelo.EntityFrameworkCore.MySql`.

```csharp
// 1. DbContext-inizi FACTORY kimi qeydiyyatdan keçirin (adi AddDbContext yox) —
//    EfCoreIdempotencyStore singleton-dır və IDbContextFactory<TContext> tələb edir.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddIdempo();
builder.Services.AddIdempoEntityFrameworkCoreStore<AppDbContext>();
```

```csharp
// 2. Cədvəli mövcud modelinizə əlavə edin — bir cədvəl daha, ayrıca baza deyil.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ConfigureIdempotencyStore();
}
```

```bash
# 3. Cədvəli yaradın — ConfigureIdempotencyStore() yalnız EF Core *modelini* yeniləyir.
dotnet ef migrations add AddIdempotencyRecords
dotnet ef database update
```

Artıq öz entity-ləriniz üçün başqa yerdə `AddDbContext<AppDbContext>(...)` çağırırsınız? Bu
qeydiyyat tək başına bu paket üçün kifayət deyil — yanına `AddDbContextFactory<AppDbContext>(...)`
əlavə edin; hər ikisi eyni `AppDbContext` sinfinə və verilənlər bazasına təhlükəsiz işarə edə bilər.

## Tam sənədləşdirmə

Yuxarıdakı tez-tez rast gəlinən səhvlərin tam izahı, versiya uyğunluq matrisi, və
`IIdempotencyStore`-un atomiklik zəmanətinin burada necə implementasiya olunduğu (cədvəlin primary
key-i + EF Core-un optimistic concurrency yoxlaması — heç bir xam SQL) üçün
[layihə README-inə](https://github.com/altayaliev/Idempo#readme) baxın. İngiliscə və
azərbaycanca mövcuddur.
