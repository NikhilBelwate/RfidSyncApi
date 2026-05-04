using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Domain.Entities;
using RfidSyncApi.Infrastructure.Persistence;

namespace RfidSyncApi.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRfidLogRepository"/>.
///
/// Performance principles applied:
///   • All queries are async to avoid thread-pool starvation under load.
///   • Bulk reads use .Where(id IN list) to avoid N+1 round-trips.
///   • Bulk inserts delegate batching to EF Core 8 (default batch size = 42,
///     overridable in DbContext options).
///   • No tracking for read-only queries (AsNoTracking) to reduce memory overhead.
/// </summary>
public class RfidLogRepository : IRfidLogRepository
{
    private readonly ApplicationDbContext _db;

    public RfidLogRepository(ApplicationDbContext db) => _db = db;

    // ── Read helpers ──────────────────────────────────────────────────────────

    public async Task<Dictionary<string, RfidLog>> GetExistingByLocalIdsAsync(
        string deviceId,
        IEnumerable<string> localIds,
        CancellationToken ct = default)
    {
        var idList = localIds.ToList();
        if (idList.Count == 0) return new Dictionary<string, RfidLog>();

        return await _db.RfidLogs
            .Where(l => l.DeviceId == deviceId && idList.Contains(l.LocalId))
            .ToDictionaryAsync(l => l.LocalId, ct);
    }

    public async Task<Dictionary<Guid, RfidLog>> GetExistingByServerIdsAsync(
        IEnumerable<Guid> serverIds,
        CancellationToken ct = default)
    {
        var idList = serverIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, RfidLog>();

        return await _db.RfidLogs
            .Where(l => idList.Contains(l.ServerId))
            .ToDictionaryAsync(l => l.ServerId, ct);
    }

    // ── Write helpers ─────────────────────────────────────────────────────────

    public async Task BulkInsertAsync(IEnumerable<RfidLog> logs, CancellationToken ct = default)
    {
        await _db.RfidLogs.AddRangeAsync(logs, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    // ── Delta / server_changes query ──────────────────────────────────────────

    public async Task<(List<RfidLog> Items, int TotalCount)> GetServerChangesAsync(
        string excludeDeviceId,
        DateTime since,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = _db.RfidLogs
            .AsNoTracking()
            .Where(l => l.DeviceId != excludeDeviceId && l.UpdatedAt > since)
            .OrderBy(l => l.UpdatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip(skip).Take(take).ToListAsync(ct);

        return (items, total);
    }

    // ── GET /api/logs ─────────────────────────────────────────────────────────

    public async Task<(List<RfidLog> Items, int TotalCount)> GetLogsAsync(
        string? siteId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.RfidLogs
            .AsNoTracking()
            .Where(l => !l.IsDeleted);

        if (!string.IsNullOrWhiteSpace(siteId))
            query = query.Where(l => l.SiteId == siteId);

        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);

        var total = await query.CountAsync(ct);
        var skip = (page - 1) * pageSize;

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // ── GET /api/sessions ─────────────────────────────────────────────────────

    public async Task<List<RfidLog>> GetCheckInOutLogsAsync(
        string? deviceId,
        CancellationToken ct = default)
    {
        var query = _db.RfidLogs
            .AsNoTracking()
            .Where(l => !l.IsDeleted &&
                        (l.EventType == "CHECK_IN" || l.EventType == "CHECK_OUT"));

        if (!string.IsNullOrWhiteSpace(deviceId))
            query = query.Where(l => l.DeviceId == deviceId);

        // Order for deterministic pairing: device → tag → time ascending
        return await query
            .OrderBy(l => l.DeviceId)
            .ThenBy(l => l.TagId)
            .ThenBy(l => l.CreatedAt)
            .ToListAsync(ct);
    }
}
