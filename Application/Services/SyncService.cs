using Microsoft.Extensions.Options;
using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Domain.Entities;
using RfidSyncApi.Infrastructure.Configuration;
using RfidSyncApi.Infrastructure.Repositories;

namespace RfidSyncApi.Application.Services;

/// <summary>
/// Core sync orchestration service.
///
/// Design principles:
///   1. No per-row DB round-trips — all lookups are single batch queries.
///   2. INSERT, UPDATE, and DELETE are processed in separate passes to maximise
///      batching efficiency.
///   3. Conflict resolution is pure in-memory logic applied after the batch
///      lookup, before any write.
///   4. server_changes are paginated so the HTTP response stays bounded.
/// </summary>
public class SyncService : ISyncService
{
    private readonly IRfidLogRepository _repo;
    private readonly ApiSettings _settings;
    private readonly ILogger<SyncService> _logger;

    // Conflict resolution outcome labels
    private const string Success = "SUCCESS";
    private const string ConflictClientWins = "CONFLICT_CLIENT_WINS";
    private const string ConflictServerWins = "CONFLICT_SERVER_WINS";
    
    private const string Error = "ERROR";

    public SyncService(
        IRfidLogRepository repo,
        IOptions<ApiSettings> settings,
        ILogger<SyncService> logger)
    {
        _repo = repo;
        _settings = settings.Value;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ProcessSyncAsync – main entry point
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<SyncResponse> ProcessSyncAsync(
        SyncRequest request,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = new SyncResponse { ServerTime = DateTime.UtcNow };

        _logger.LogInformation(
            "Sync started. DeviceId={DeviceId} BatchSize={BatchSize} LastSync={LastSync}",
            request.DeviceId, request.Changes.Count, request.LastSyncTime);

        // ── 1. Partition changes by effective operation (defaults to INSERT) ────
        var inserts = request.Changes
            .Where(c => c.EffectiveOperation == "INSERT")
            .ToList();

        var updates = request.Changes
            .Where(c => c.EffectiveOperation == "UPDATE")
            .ToList();

        var deletes = request.Changes
            .Where(c => c.EffectiveOperation == "DELETE")
            .ToList();

        // ── 2. Batch-load existing records (2 queries total) ──────────────────
        var insertLocalIds = inserts.Select(c => c.LocalId);
        var updateServerIds = updates
            .Where(c => c.ServerId.HasValue).Select(c => c.ServerId!.Value);
        var deleteServerIds = deletes
            .Where(c => c.ServerId.HasValue).Select(c => c.ServerId!.Value);

        var (existingByLocal, existingByServer) = await LoadExistingRecordsAsync(
            request.DeviceId, insertLocalIds, updateServerIds.Concat(deleteServerIds), ct);

        // ── 3. Process each partition ─────────────────────────────────────────
        var newLogs = new List<RfidLog>();
        var results = new List<ChangeResult>();

        ProcessInserts(inserts, existingByLocal, newLogs, results, request.DeviceId);
        ProcessUpdates(updates, existingByServer, results);
        ProcessDeletes(deletes, existingByServer, results);

        // ── 4. Write new records in one batch insert ──────────────────────────
        if (newLogs.Count > 0)
            await _repo.BulkInsertAsync(newLogs, ct);

        // ── 5. Save updated / soft-deleted records ────────────────────────────
        await _repo.SaveChangesAsync(ct);

        // ── 6. Build ordered response results (preserves client ordering) ─────
        response.Results = BuildOrderedResults(request.Changes, results);

        // ── 7. Fetch server_changes (delta since last_sync_time, page 0) ──────
        //    last_sync_time == 0 means "first sync" — skip the delta pull
        if (request.LastSyncTime > 0)
        {
            var since = EpochMsToUtc(request.LastSyncTime);
            var (serverChanges, nextToken) = await GetServerChangesPageAsync(
                request.DeviceId, since, pageToken: null, ct);

            response.ServerChanges = serverChanges;
            response.NextPageToken = nextToken;
        }

        sw.Stop();
        _logger.LogInformation(
            "Sync completed. DeviceId={DeviceId} Inserts={Inserts} Updates={Updates} Deletes={Deletes} ElapsedMs={Elapsed}",
            request.DeviceId, newLogs.Count,
            updates.Count, deletes.Count, sw.ElapsedMilliseconds);

        return response;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GetServerChangesPageAsync – paginated delta fetch
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<(List<ServerChange> Changes, string? NextPageToken)> GetServerChangesPageAsync(
        string deviceId,
        DateTime since,
        string? pageToken,
        CancellationToken ct = default)
    {
        var skip = DecodePageToken(pageToken);
        var take = _settings.ServerChangesPageSize;

        var (items, total) = await _repo.GetServerChangesAsync(deviceId, since, skip, take, ct);

        var changes = items.Select(MapToServerChange).ToList();

        var nextSkip = skip + items.Count;
        var nextToken = nextSkip < total ? EncodePageToken(nextSkip) : null;

        return (changes, nextToken);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GetLogsAsync – GET /api/logs
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<LogsResponse> GetLogsAsync(
        string? siteId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetLogsAsync(siteId, from, to, page, pageSize, ct);

        return new LogsResponse
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Items = items.Select(MapToLogItem).ToList()
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<(Dictionary<string, RfidLog>, Dictionary<Guid, RfidLog>)> LoadExistingRecordsAsync(
        string deviceId,
        IEnumerable<string> localIds,
        IEnumerable<Guid> serverIds,
        CancellationToken ct)
    {
        var byLocal = await _repo.GetExistingByLocalIdsAsync(deviceId, localIds, ct);
        var byServer = await _repo.GetExistingByServerIdsAsync(serverIds, ct);
        return (byLocal, byServer);
    }

    // ── INSERT ────────────────────────────────────────────────────────────────

    private void ProcessInserts(
        List<ChangeRecord> inserts,
        Dictionary<string, RfidLog> existingByLocal,
        List<RfidLog> newLogs,
        List<ChangeResult> results,
        string deviceId)
    {
        foreach (var change in inserts)
        {
            try
            {
                var iKey = ComputeIdempotencyKey(
                    deviceId, change.LocalId, change.TagId,
                    change.EventType, change.UserId ?? string.Empty, change.CreatedAt);

                if (existingByLocal.TryGetValue(change.LocalId, out var existing))
                {
                    if (existing.IdempotencyKey == iKey)
                    {
                        // ── True idempotent retry: identical payload resent ────────
                        // Mark the existing record SYNCED (idempotent — no-op if already SYNCED)
                        existing.SyncStatus = "SYNCED";

                        _logger.LogDebug(
                            "Idempotent re-submit. DeviceId={DeviceId} LocalId={LocalId} ServerId={ServerId}",
                            deviceId, change.LocalId, existing.ServerId);

                        results.Add(new ChangeResult
                        {
                            LocalId    = change.LocalId,
                            ServerId   = existing.ServerId,
                            Status     = "SYNCED",
                            SyncStatus = "SYNCED",
                            Message    = "Idempotent re-submit — record already accepted on server."
                        });
                    }
                    else
                    {
                        // ── Same local_id but different content — data integrity issue ──
                        _logger.LogWarning(
                            "Idempotency conflict. DeviceId={DeviceId} LocalId={LocalId}: " +
                            "existing key={ExistingKey} incoming key={IncomingKey}",
                            deviceId, change.LocalId, existing.IdempotencyKey, iKey);

                        results.Add(ErrorResult(change.LocalId,
                            "Idempotency conflict: local_id already exists with different content. " +
                            "Use operation=UPDATE with server_id to modify an existing record."));
                    }
                    continue;
                }

                // ── New record: stamp SYNCED immediately — validation already passed ──
                var log = new RfidLog
                {
                    ServerId        = Guid.NewGuid(),
                    DeviceId        = deviceId,
                    LocalId         = change.LocalId,
                    TagId           = change.TagId,
                    UserId          = change.UserId ?? string.Empty,
                    SiteId          = change.SiteId ?? string.Empty,
                    EventType       = change.EventType,
                    CreatedAt       = EpochMsToUtc(change.CreatedAt),
                    UpdatedAt       = EpochMsToUtc(change.UpdatedAt),
                    Version         = change.Version,
                    IsDeleted       = false,
                    IdempotencyKey  = iKey,
                    SyncStatus      = "SYNCED"   // server confirms acceptance
                };

                newLogs.Add(log);
                results.Add(new ChangeResult
                {
                    LocalId    = change.LocalId,
                    ServerId   = log.ServerId,
                    Status     = Success,
                    SyncStatus = "SYNCED"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing INSERT for LocalId={LocalId}", change.LocalId);
                results.Add(ErrorResult(change.LocalId, ex.Message));
            }
        }
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    private void ProcessUpdates(
        List<ChangeRecord> updates,
        Dictionary<Guid, RfidLog> existingByServer,
        List<ChangeResult> results)
    {
        foreach (var change in updates)
        {
            try
            {
                if (change.ServerId is null)
                {
                    results.Add(ErrorResult(change.LocalId, "UPDATE requires server_id."));
                    continue;
                }

                if (!existingByServer.TryGetValue(change.ServerId.Value, out var existing))
                {
                    results.Add(ErrorResult(change.LocalId,
                        $"Record {change.ServerId} not found on server."));
                    continue;
                }

                var resolution = ResolveConflict(existing, change);

                switch (resolution)
                {
                    case ConflictResolution.Accept:
                        ApplyUpdate(existing, change);   // sets SyncStatus = "SYNCED" on entity
                        results.Add(new ChangeResult
                        {
                            LocalId    = change.LocalId,
                            ServerId   = existing.ServerId,
                            Status     = Success,
                            SyncStatus = "SYNCED"
                        });
                        break;

                    case ConflictResolution.ClientWins:
                        ApplyUpdate(existing, change);   // sets SyncStatus = "SYNCED" on entity
                        results.Add(new ChangeResult
                        {
                            LocalId    = change.LocalId,
                            ServerId   = existing.ServerId,
                            Status     = ConflictClientWins,
                            SyncStatus = "SYNCED",
                            Message    = "Versions equal; client's updated_at was newer — client wins."
                        });
                        break;

                    case ConflictResolution.ServerWins:
                        // Client record was NOT applied — do not mark SYNCED.
                        // Device must fetch server_data and re-submit with updated version.
                        results.Add(new ChangeResult
                        {
                            LocalId    = change.LocalId,
                            ServerId   = existing.ServerId,
                            Status     = ConflictServerWins,
                            SyncStatus = null,   // explicitly null: client is out of date
                            Message    = "Server version is newer — client must re-sync.",
                            ServerData = MapToServerChangeData(existing)
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing UPDATE for LocalId={LocalId}", change.LocalId);
                results.Add(ErrorResult(change.LocalId, ex.Message));
            }
        }
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    private void ProcessDeletes(
        List<ChangeRecord> deletes,
        Dictionary<Guid, RfidLog> existingByServer,
        List<ChangeResult> results)
    {
        foreach (var change in deletes)
        {
            try
            {
                if (change.ServerId is null)
                {
                    results.Add(ErrorResult(change.LocalId, "DELETE requires server_id."));
                    continue;
                }

                if (!existingByServer.TryGetValue(change.ServerId.Value, out var existing))
                {
                    // Idempotent: already deleted or never existed
                    results.Add(new ChangeResult
                    {
                        LocalId = change.LocalId,
                        ServerId = change.ServerId,
                        Status = "FAILED",
                        Message = "Record not found — possibly already deleted."
                    });
                    continue;
                }

                // Soft delete — stamp SYNCED to confirm the delete was accepted
                existing.IsDeleted  = true;
                existing.UpdatedAt  = DateTime.UtcNow;
                existing.Version++;
                existing.SyncStatus = "SYNCED";

                results.Add(new ChangeResult
                {
                    LocalId    = change.LocalId,
                    ServerId   = existing.ServerId,
                    Status     = Success,
                    SyncStatus = "SYNCED"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DELETE for LocalId={LocalId}", change.LocalId);
                results.Add(ErrorResult(change.LocalId, ex.Message));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Conflict Resolution
    // ══════════════════════════════════════════════════════════════════════════

    private enum ConflictResolution { Accept, ClientWins, ServerWins }

    /// <summary>
    /// Conflict rules:
    ///   incoming.version &gt; server.version  → Accept (client advances version)
    ///   incoming.version &lt; server.version  → ServerWins (reject stale client)
    ///   incoming.version == server.version  → last-write-wins on updated_at
    ///                                          tie → ServerWins (conservative)
    /// </summary>
    private static ConflictResolution ResolveConflict(RfidLog server, ChangeRecord incoming)
    {
        if (incoming.Version > server.Version) return ConflictResolution.Accept;
        if (incoming.Version < server.Version) return ConflictResolution.ServerWins;

        // Equal versions — last-write-wins on updated_at (epoch ms → DateTime for comparison)
        if (EpochMsToUtc(incoming.UpdatedAt) > server.UpdatedAt) return ConflictResolution.ClientWins;
        return ConflictResolution.ServerWins; // tie-break: server wins
    }

    private static void ApplyUpdate(RfidLog target, ChangeRecord source)
    {
        target.TagId      = source.TagId;
        target.UserId     = source.UserId    ?? target.UserId;
        target.SiteId     = source.SiteId    ?? target.SiteId;
        target.EventType  = source.EventType;
        target.UpdatedAt  = DateTime.UtcNow; // always stamp with server time
        target.Version    = source.Version;
        target.SyncStatus = "SYNCED";        // server confirms this version is persisted
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Mapping helpers
    // ══════════════════════════════════════════════════════════════════════════

    private static ServerChange MapToServerChange(RfidLog log) => new()
    {
        ServerId = log.ServerId,
        Operation = log.IsDeleted ? "DELETE" : "UPDATE",
        Data = MapToServerChangeData(log)
    };

    private static ServerChangeData MapToServerChangeData(RfidLog log) => new()
    {
        TagId = log.TagId,
        UserId = log.UserId,
        SiteId = log.SiteId,
        EventType = log.EventType,
        CreatedAt = log.CreatedAt,
        UpdatedAt = log.UpdatedAt,
        Version = log.Version,
        IsDeleted = log.IsDeleted
    };

    private static LogItem MapToLogItem(RfidLog log) => new()
    {
        ServerId = log.ServerId,
        DeviceId = log.DeviceId,
        LocalId = log.LocalId,
        TagId = log.TagId,
        UserId = log.UserId,
        SiteId = log.SiteId,
        EventType = log.EventType,
        CreatedAt = log.CreatedAt,
        UpdatedAt = log.UpdatedAt,
        Version = log.Version,
        IsDeleted = log.IsDeleted
    };

    private static ChangeResult ErrorResult(string localId, string message) => new()
    {
        LocalId = localId,
        Status = Error,
        Message = message
    };

    /// <summary>
    /// Preserves the client's original ordering in the response so the device
    /// can zip results with its input array by index.
    /// </summary>
    private static List<ChangeResult> BuildOrderedResults(
        List<ChangeRecord> ordered,
        List<ChangeResult> results)
    {
        var resultMap = results.ToDictionary(r => r.LocalId);
        return ordered
            .Select(c => resultMap.TryGetValue(c.LocalId, out var r) ? r
                         : new ChangeResult { LocalId = c.LocalId, Status = Error, Message = "Not processed." })
            .ToList();
    }

    // ── Page token (simple base64-encoded skip count) ─────────────────────────

    private static string EncodePageToken(int skip)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(skip.ToString()));

    private static int DecodePageToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return 0;
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return int.TryParse(decoded, out var skip) ? skip : 0;
        }
        catch { return 0; }
    }

    // ── Timestamp helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a Unix epoch millisecond value (as sent by Android clients) to a
    /// UTC <see cref="DateTime"/> suitable for EF Core / SQL Server storage.
    /// </summary>
    private static DateTime EpochMsToUtc(long epochMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;

    // ── Idempotency key ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes a 64-char lowercase SHA-256 hex digest from the five business-key
    /// fields that uniquely identify a physical scan event, plus the client timestamp.
    ///
    /// Key space: device_id | local_id | tag_id | event_type | user_id | created_at_ms
    ///
    /// Properties:
    ///   • Deterministic — same inputs always produce the same key.
    ///   • Collision-resistant — SHA-256 makes accidental or adversarial clashes negligible.
    ///   • Compact — 64 hex chars fit in nvarchar(64) with no padding.
    /// </summary>
    private static string ComputeIdempotencyKey(
        string deviceId, string localId, string tagId,
        string eventType, string userId, long createdAtMs)
    {
        var raw = $"{deviceId}|{localId}|{tagId}|{eventType}|{userId}|{createdAtMs}";
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant(); // 64-char hex
    }
}
