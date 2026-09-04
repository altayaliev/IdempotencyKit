using Microsoft.EntityFrameworkCore;

namespace Idempo.EntityFrameworkCore.Tests;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureIdempotencyStore();
}
