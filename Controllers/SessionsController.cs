using Microsoft.AspNetCore.Mvc;
using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Application.Services;

namespace RfidSyncApi.Controllers;

/// <summary>
/// GET /api/sessions — CHECK_IN / CHECK_OUT session duration analytics.
///
/// Pairs every CHECK_IN with the next CHECK_OUT for the same tag on the same
/// device, computes duration, and bundles all transactions into one record
/// per device.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionService sessionService,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _logger         = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GET /api/sessions
    //  GET /api/sessions?device_id={deviceId}
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns CHECK_IN / CHECK_OUT session summaries grouped by device.
    ///
    /// When <c>device_id</c> is omitted, all devices are returned.
    /// When supplied, only that device's sessions are included.
    ///
    /// Each session record contains:
    ///   - tag_id / user_id that performed the scans
    ///   - check_in_time and check_out_time (UTC)
    ///   - duration_seconds, duration_minutes, duration_formatted
    ///   - status: COMPLETED | OPEN | ORPHANED
    ///
    /// The device-level envelope additionally provides:
    ///   - total / completed / open / orphaned session counts
    ///   - total, average, max and min duration in minutes
    /// </summary>
    /// <param name="deviceId">Optional device filter (e.g. device-alpha-001).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(SessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessions(
        [FromQuery(Name = "device_id")] string? deviceId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Sessions request. DeviceFilter={DeviceFilter}",
            deviceId ?? "ALL");

        var result = await _sessionService.GetSessionsAsync(deviceId, ct);

        // If a specific device was requested and nothing came back, return 404
        if (!string.IsNullOrWhiteSpace(deviceId) && result.TotalDevices == 0)
        {
            _logger.LogWarning(
                "Sessions request: no CHECK_IN/OUT records found for device {DeviceId}.",
                deviceId);

            return NotFound(new
            {
                error   = "DeviceNotFound",
                message = $"No CHECK_IN or CHECK_OUT records found for device_id '{deviceId}'."
            });
        }

        _logger.LogInformation(
            "Sessions response: {Devices} device(s), {Sessions} total sessions.",
            result.TotalDevices,
            result.Devices.Sum(d => d.TotalSessions));

        return Ok(result);
    }
}
