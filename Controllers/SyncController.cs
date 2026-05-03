using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Application.Services;

namespace RfidSyncApi.Controllers;

/// <summary>
/// POST /api/sync  — primary offline-first sync endpoint.
///
/// Designed for high-throughput batch ingestion from Android devices in remote
/// industrial environments.  A single call can carry up to 10 000 change records.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly IValidator<SyncRequest> _validator;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ISyncService syncService,
        IValidator<SyncRequest> validator,
        ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _validator = validator;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  POST /api/sync
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Accepts a batch of offline RFID changes from a device and returns the
    /// per-record sync results plus any server-side delta the device hasn't seen.
    /// </summary>
    /// <param name="request">Sync payload — up to 10 000 changes.</param>
    /// <param name="ct">Cancellation token (wired to client disconnect).</param>
    [HttpPost]
    [ProducesResponseType(typeof(SyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [RequestSizeLimit(52_428_800)] // 50 MB hard cap
    public async Task<IActionResult> Sync(
        [FromBody] SyncRequest request,
        CancellationToken ct)
    {
        // ── FluentValidation ──────────────────────────────────────────────────
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Sync validation failed. DeviceId={DeviceId} Errors={Errors}",
                request.DeviceId,
                string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem();
        }

        // ── Log inbound telemetry ─────────────────────────────────────────────
        _logger.LogInformation(
            "Sync request received. DeviceId={DeviceId} BatchSize={BatchSize} ContentLength={ContentLength}",
            request.DeviceId,
            request.Changes.Count,
            Request.ContentLength);

        var response = await _syncService.ProcessSyncAsync(request, ct);

        return Ok(response);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GET /api/sync/changes  — paginated server_changes pull
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a page of server-side changes the device hasn't synced yet.
    /// Clients should call this when they receive a non-null next_page_token
    /// in the POST /api/sync response.
    /// </summary>
    [HttpGet("changes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetServerChanges(
        [FromQuery] string deviceId,
        [FromQuery] DateTime since,
        [FromQuery] string? pageToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new { error = "deviceId query parameter is required." });

        var (changes, nextToken) = await _syncService.GetServerChangesPageAsync(
            deviceId, since, pageToken, ct);

        return Ok(new
        {
            server_time = DateTime.UtcNow,
            server_changes = changes,
            next_page_token = nextToken
        });
    }
}
