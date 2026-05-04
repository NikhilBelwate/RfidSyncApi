using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Domain.Entities;

namespace RfidSyncApi.Infrastructure.Persistence;

/// <summary>
/// Single EF Core DbContext for the RFID Sync API.
/// Configured for Azure SQL Database.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<RfidLog> RfidLogs => Set<RfidLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RfidLog>(entity =>
        {
            // ── Primary key ─────────────────────────────────────────────────
            entity.HasKey(e => e.ServerId);
            entity.Property(e => e.ServerId)
                  .HasDefaultValueSql("NEWSEQUENTIALID()"); // clustered-friendly on Azure SQL

            // ── Unique constraint: one server record per (device, local) pair ──
            entity.HasIndex(e => new { e.DeviceId, e.LocalId })
                  .IsUnique()
                  .HasDatabaseName("UX_rfid_logs_device_local");

            // ── Unique constraint: content-based idempotency key ─────────────
            //    SHA-256(device_id|local_id|tag_id|event_type|user_id|created_at_ms)
            //    Catches concurrent duplicate submissions that bypass the in-memory check.
            entity.HasIndex(e => e.IdempotencyKey)
                  .IsUnique()
                  .HasDatabaseName("UX_rfid_logs_idempotency_key");

            // ── Query indexes ────────────────────────────────────────────────
            entity.HasIndex(e => e.DeviceId)
                  .HasDatabaseName("IX_rfid_logs_device_id");

            entity.HasIndex(e => e.UpdatedAt)
                  .HasDatabaseName("IX_rfid_logs_updated_at");

            entity.HasIndex(e => e.TagId)
                  .HasDatabaseName("IX_rfid_logs_tag_id");

            // Composite index for server_changes delta pull (most common query)
            entity.HasIndex(e => new { e.DeviceId, e.UpdatedAt })
                  .HasDatabaseName("IX_rfid_logs_device_updated");

            // site_id — used for filtering and future partitioning
            entity.HasIndex(e => e.SiteId)
                  .HasDatabaseName("IX_rfid_logs_site_id");

            // ── Column constraints ───────────────────────────────────────────
            entity.Property(e => e.TagId)
                  .IsRequired()
                  .HasMaxLength(64);

            entity.Property(e => e.EventType)
                  .IsRequired()
                  .HasMaxLength(64);

            entity.Property(e => e.Version)
                  .HasDefaultValue(1)
                  .IsConcurrencyToken();  // EF optimistic concurrency hook
        });
    }
}
