using RfidSyncApi.Domain.Entities;

namespace RfidSyncApi.Infrastructure.Repositories;

/// <summary>
/// Repository contract for RfidLog data access.
/// The interface lives in Infrastructure (not Application) because this project
/// keeps the boundary simple — promote to Application layer in larger codebases.
/// </summary>
public interface IRfidLogRepository
{
    /// <summary>
    /// Returns existing logs for the given (deviceId, localId) pairs in a single query.
    /// Used to decide INSERT vs UPDATE during batch sync.
    /// </summary>
    Task<Dictionary<string, RfidLog>> GetExistingByLocalIdsAsync(
        string deviceId,
        IEnumerable<string> localIds,
        CancellationToken ct = default);

    /// <summary>
    /// Returns existing logs by their server IDs.
    /// Used for UPDATE / DELETE operations where the client already knows the server_id.
    /// </summary>
    Task<Dictionary<Guid, RfidLog>> GetExistingByServerIdsAsync(
        IEnumerable<Guid> serverIds,
        CancellationToken ct = default);

    /// <summary>
    /// Bulk-inserts new RFID log records using EF Core's AddRange + SaveChanges.
    /// EF Core 8 batches these into efficient multi-row INSERT statements.
    /// </summary>
    Task BulkInsertAsync(IEnumerable<RfidLog> logs, CancellationToken ct = default);

    /// <summary>
    /// Saves all pending EF Core changes (UPDATE / soft-DELETE operations applied
    /// directly to tracked entities before calling this).
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns server-side records that changed after <paramref name="since"/>,
    /// excluding records from the requesting device (it already has those).
    /// Paginated via skip/take for large delta sets.
    /// </summary>
    Task<(List<RfidLog> Items, int TotalCount)> GetServerChangesAsync(
        string excludeDeviceId,
        DateTime since,
        int skip,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Paginated query for GET /api/logs with optional filters.
    /// </summary>
    Task<(List<RfidLog> Items, int TotalCount)> GetLogsAsync(
        string? siteId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all non-deleted CHECK_IN and CHECK_OUT records, optionally
    /// filtered to a single device.  Used by the sessions computation service.
    /// Ordered by device_id → tag_id → created_at ASC for deterministic pairing.
    /// </summary>
    Task<List<RfidLog>> GetCheckInOutLogsAsync(
        string? deviceId,
        CancellationToken ct = default);
}
