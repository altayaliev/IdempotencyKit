using Microsoft.EntityFrameworkCore;

namespace IdempotencyKit.EntityFrameworkCore;

public static class IdempotencyModelBuilderExtensions
{
    /// <summary>
    /// Maps the table <see cref="EfCoreIdempotencyStore{TContext}"/> reads and writes. Call this
    /// from your <c>DbContext.OnModelCreating</c> — no <c>DbSet</c> property is required, the
    /// store accesses the entity via <c>context.Set&lt;IdempotencyRecordEntity&gt;()</c>.
    /// </summary>
    public static ModelBuilder ConfigureIdempotencyStore(this ModelBuilder modelBuilder, string tableName = "IdempotencyRecords")
    {
        modelBuilder.Entity<IdempotencyRecordEntity>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(r => r.Key);
            entity.Property(r => r.Key).HasMaxLength(200);
            entity.Property(r => r.FingerprintHash).IsRequired().HasMaxLength(64);
            entity.Property(r => r.ContentType).HasMaxLength(200);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(r => r.ExpiresAtUnixMs);

            // Lets a delete-if-still-expired be expressed as a normal tracked SaveChangesAsync
            // (EF adds ExpiresAtUnixMs to the DELETE's WHERE clause and throws on a 0-row
            // mismatch) rather than ExecuteDeleteAsync.
            entity.Property(r => r.ExpiresAtUnixMs).IsConcurrencyToken();
        });

        return modelBuilder;
    }
}
