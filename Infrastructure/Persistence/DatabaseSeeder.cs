using RfidSyncApi.Domain.Entities;

namespace RfidSyncApi.Infrastructure.Persistence;

/// <summary>
/// Seeds sample RFID log data for development / demo environments.
/// Call only when ASPNETCORE_ENVIRONMENT == Development or when an explicit
/// --seed flag is passed at startup.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        if (db.RfidLogs.Any())
        {
            logger.LogInformation("Database already seeded — skipping.");
            return;
        }

        var devices = new[] { "device-001", "device-002", "device-003" };
        var sites = new[] { "site-ALPHA", "site-BETA", "site-GAMMA" };
        var users = new[] { "user-john", "user-jane", "user-bob" };
        var events = new[] { "CHECK_IN", "CHECK_OUT", "INSPECTION", "MAINTENANCE" };

        var rng = new Random(42);
        var now = DateTime.UtcNow;
        var logs = new List<RfidLog>();

        for (var i = 0; i < 200; i++)
        {
            var createdAt = now.AddHours(-rng.Next(1, 720)); // last 30 days
            logs.Add(new RfidLog
            {
                ServerId = Guid.NewGuid(),
                DeviceId = devices[rng.Next(devices.Length)],
                LocalId = $"local-seed-{i:D4}",
                TagId = $"TAG{rng.Next(1000, 9999)}",
                UserId = users[rng.Next(users.Length)],
                SiteId = sites[rng.Next(sites.Length)],
                EventType = events[rng.Next(events.Length)],
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddMinutes(rng.Next(0, 60)),
                Version = rng.Next(1, 5),
                IsDeleted = rng.Next(0, 20) == 0 // ~5% soft-deleted
            });
        }

        await db.RfidLogs.AddRangeAsync(logs);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} RFID log records.", logs.Count);
    }
}
