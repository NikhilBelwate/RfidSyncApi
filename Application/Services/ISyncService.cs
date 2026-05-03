using RfidSyncApi.Application.DTOs;

namespace RfidSyncApi.Application.Services;

/// <summary>Interface contract for the sync service — enables easy mocking in tests.</summary>
public interface ISyncService
{
    Task<SyncResponse> ProcessSyncAsync(SyncRequest request, CancellationToken ct = default);

    Task<(List<ServerChange> Changes, string? NextPageToken)> GetServerChangesPageAsync(
        string deviceId,
        DateTime since,
        string? pageToken,
        CancellationToken ct = default);

    Task<LogsResponse> GetLogsAsync(
        string? siteId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
