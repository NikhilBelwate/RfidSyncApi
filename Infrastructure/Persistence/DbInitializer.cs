using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Domain.Entities;

namespace RfidSyncApi.Infrastructure.Persistence;

/// <summary>
/// Production-grade database initializer.
///
/// Responsibilities (all idempotent — safe to call on every startup):
///   1. Verify connectivity to Azure SQL.
///   2. Apply any pending EF Core migrations (creates schema on first run).
///   3. Seed reference / lookup data (event types, known devices).
///   4. Optionally seed sample RFID scan logs for demo / dev environments.
///
/// Call order in Program.cs:
///   await DbInitializer.InitializeAsync(app.Services, app.Logger, seedSampleData: isDev);
/// </summary>
public static class DbInitializer
{
    // ── Known event types (lookup reference data) ─────────────────────────────
    private static readonly string[] EventTypes =
        { "CHECK_IN", "CHECK_OUT", "INSPECTION", "MAINTENANCE", "AUDIT", "TRANSFER" };

    // ── Reference devices (fleet registry) ───────────────────────────────────
    private static readonly (string DeviceId, string SiteId)[] ReferenceDevices =
    {
        ("device-alpha-001", "site-ALPHA"),
        ("device-alpha-002", "site-ALPHA"),
        ("device-beta-001",  "site-BETA"),
        ("device-beta-002",  "site-BETA"),
        ("device-gamma-001", "site-GAMMA")
    };

    // ── Reference users ───────────────────────────────────────────────────────
    private static readonly string[] Users =
        { "user-john-smith", "user-jane-doe", "user-bob-jones", "user-alice-wu", "user-carlos-m" };

    // ══════════════════════════════════════════════════════════════════════════
    //  Public entry point
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Main initialization entry point. Call once at application startup.
    /// </summary>
    /// <param name="services">Root service provider from the built WebApplication.</param>
    /// <param name="logger">Application-level logger.</param>
    /// <param name="seedSampleData">
    ///   When true, inserts 200 sample RFID scan records for demo / development use.
    ///   Always false in production unless explicitly overridden.
    /// </param>
    public static async Task InitializeAsync(
        IServiceProvider services,
        ILogger logger,
        bool seedSampleData = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            // ── Step 1: Connectivity check ────────────────────────────────────
            logger.LogInformation("DbInitializer: Checking Azure SQL connectivity…");
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogError("DbInitializer: Cannot connect to Azure SQL. Check connection string.");
                throw new InvalidOperationException(
                    "Database connectivity check failed. " +
                    "Verify DefaultConnection in appsettings / App Service config.");
            }
            logger.LogInformation("DbInitializer: Azure SQL connection OK ✓");

            // ── Step 2: Apply pending migrations ──────────────────────────────
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation(
                    "DbInitializer: Applying {Count} pending migration(s): {Names}",
                    pending.Count, string.Join(", ", pending));

                await db.Database.MigrateAsync();

                logger.LogInformation("DbInitializer: Migrations applied ✓");
            }
            else
            {
                logger.LogInformation("DbInitializer: Schema is up-to-date — no migrations needed ✓");
            }

            // ── Step 3: Seed sample data (dev / demo only) ────────────────────
            if (seedSampleData)
            {
                await SeedSampleLogsAsync(db, logger);
            }
            else
            {
                logger.LogInformation(
                    "DbInitializer: Sample data seeding skipped (not a dev/demo environment).");
            }

            logger.LogInformation("DbInitializer: Initialization complete ✓");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "DbInitializer: Initialization failed.");
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Seed: sample RFID scan logs
    // ══════════════════════════════════════════════════════════════════════════

    private static async Task SeedSampleLogsAsync(
        ApplicationDbContext db,
        ILogger logger)
    {
        var existingCount = await db.RfidLogs.CountAsync();
        if (existingCount > 0)
        {
            logger.LogInformation(
                "DbInitializer: {Count} records already present — skipping sample seed.", existingCount);
            return;
        }

        logger.LogInformation("DbInitializer: Seeding sample RFID log data…");

        var rng = new Random(42); // deterministic seed for reproducible demo data
        var now = DateTime.UtcNow;
        var logs = new List<RfidLog>();

        // ── 200 randomised scan events spanning the last 30 days ──────────────
        for (var i = 0; i < 200; i++)
        {
            var (deviceId, siteId) = ReferenceDevices[rng.Next(ReferenceDevices.Length)];
            var createdAt = now.AddHours(-rng.Next(1, 720)); // last 30 days
            var updatedAt = createdAt.AddMinutes(rng.Next(0, 30));
            var version   = rng.Next(1, 4);

            logs.Add(new RfidLog
            {
                ServerId  = Guid.NewGuid(),
                DeviceId  = deviceId,
                LocalId   = $"seed-local-{i:D4}",
                TagId     = $"TAG{rng.Next(1000, 9999):D4}",
                UserId    = Users[rng.Next(Users.Length)],
                SiteId    = siteId,
                EventType = EventTypes[rng.Next(EventTypes.Length)],
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                Version   = version,
                IsDeleted = rng.Next(0, 20) == 0  // ~5% soft-deleted for realism
            });
        }

        // ── 10 "today's" events so queries against today always return data ───
        for (var i = 0; i < 10; i++)
        {
            var (deviceId, siteId) = ReferenceDevices[i % ReferenceDevices.Length];
            var createdAt = now.AddMinutes(-rng.Next(1, 480));

            logs.Add(new RfidLog
            {
                ServerId  = Guid.NewGuid(),
                DeviceId  = deviceId,
                LocalId   = $"seed-today-{i:D4}",
                TagId     = $"TAG{9000 + i:D4}",
                UserId    = Users[i % Users.Length],
                SiteId    = siteId,
                EventType = i % 2 == 0 ? "CHECK_IN" : "CHECK_OUT",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                Version   = 1,
                IsDeleted = false
            });
        }

        await db.RfidLogs.AddRangeAsync(logs);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "DbInitializer: Seeded {Count} RFID log records across {Sites} sites and {Devices} devices ✓",
            logs.Count,
            ReferenceDevices.Select(d => d.SiteId).Distinct().Count(),
            ReferenceDevices.Length);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Utility: print DB status to logger (useful for health checks / admin)
    // ══════════════════════════════════════════════════════════════════════════

    public static async Task PrintStatusAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var total     = await db.RfidLogs.CountAsync();
        var deleted   = await db.RfidLogs.CountAsync(l => l.IsDeleted);
        var applied   = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pending   = (await db.Database.GetPendingMigrationsAsync()).ToList();
        var perDevice = await db.RfidLogs
            .GroupBy(l => l.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToListAsync();

        logger.LogInformation(
            "DB Status → Total={Total} Active={Active} SoftDeleted={Deleted} | " +
            "Migrations Applied={Applied} Pending={Pending} | " +
            "Devices={Devices}",
            total, total - deleted, deleted,
            applied.Count, pending.Count,
            string.Join(", ", perDevice.Select(d => $"{d.DeviceId}:{d.Count}")));
    }
}
