using RfidSyncApi.Application.DTOs;

namespace RfidSyncApi.Application.Services;

/// <summary>
/// Computes CHECK_IN / CHECK_OUT session durations from raw RFID log records.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Returns session summaries grouped by device.
    /// If <paramref name="deviceId"/> is supplied, only that device is returned.
    /// </summary>
    Task<SessionsResponse> GetSessionsAsync(
        string? deviceId,
        CancellationToken ct = default);
}
