using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Application.Services;

namespace RfidSyncApi.Controllers;

/// <summary>
/// POST /api/sync  — primary offline-first sync endpoint.
///
/// Designed for high-throughput batch ingestion from Android devices in remote
/// industrial environments.  A single call can carry up to 10 000 change records.
///
/// Exception handling strategy:
///   Each action wraps its service call in a typed catch ladder.
///   Every catch path:
///     1. Logs at the appropriate level (Warning for client errors, Error for server faults).
///     2. Returns a structured <see cref="ApiErrorDetail"/> JSON body with the exception
///        type, message, source file, method name, and line number (when PDB symbols
///        are present in the deployment).
///     3. Maps to a semantically correct HTTP status code.
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
        _validator   = validator;
        _logger      = logger;
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
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ApiErrorDetail), 499)]   // 499 Client Closed Request (nginx convention)
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Sync(
        [FromBody] SyncRequest request,
        CancellationToken ct)
    {
        var requestId = HttpContext.TraceIdentifier;

        try
        {
            // ── FluentValidation ──────────────────────────────────────────────
            var validation = await _validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Sync validation failed. RequestId={RequestId} DeviceId={DeviceId} Errors={Errors}",
                    requestId, request.DeviceId,
                    string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

                foreach (var error in validation.Errors)
                    ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
                return ValidationProblem();
            }

            // ── Log inbound telemetry ─────────────────────────────────────────
            _logger.LogInformation(
                "Sync request received. RequestId={RequestId} DeviceId={DeviceId} " +
                "BatchSize={BatchSize} ContentLength={ContentLength}",
                requestId, request.DeviceId,
                request.Changes.Count, Request.ContentLength);

            var response = await _syncService.ProcessSyncAsync(request, ct);

            return Ok(response);
        }

        // ── Client cancelled the request (device disconnected mid-flight) ─────
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Sync cancelled by client. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, request.DeviceId);

            // 499 Client Closed Request (nginx convention — not an official RFC status)
            return StatusCode(499, ApiErrorDetail.From(ex,
                errorCode: "CLIENT_CLOSED_REQUEST",
                requestId: requestId));
        }

        // ── Database / connectivity issues ────────────────────────────────────
        catch (DbUpdateException ex)
        {
            // Idempotency key or unique constraint violation — duplicate submission
            if (IsDuplicateKeyException(ex))
            {
                _logger.LogWarning(ex,
                    "Duplicate key on sync insert. RequestId={RequestId} DeviceId={DeviceId}",
                    requestId, request.DeviceId);

                return Conflict(ApiErrorDetail.From(ex,
                    errorCode: "DUPLICATE_RECORD",
                    requestId: requestId));
            }

            _logger.LogError(ex,
                "Database error during sync. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, request.DeviceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorDetail.From(ex,
                    errorCode: "DATABASE_ERROR",
                    requestId: requestId));
        }

        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL Server error during sync. RequestId={RequestId} DeviceId={DeviceId} SqlErrorNumber={Number}",
                requestId, request.DeviceId, ex.Number);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorDetail.From(ex,
                    errorCode: $"SQL_ERROR_{ex.Number}",
                    requestId: requestId));
        }

        // ── Bad input that passed model binding but failed in the service layer ─
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "Invalid argument in sync service. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, request.DeviceId);

            return BadRequest(ApiErrorDetail.From(ex,
                errorCode: "INVALID_ARGUMENT",
                requestId: requestId));
        }

        // ── Timeout (EF Core command timeout or downstream call) ──────────────
        catch (TimeoutException ex)
        {
            _logger.LogError(ex,
                "Timeout during sync. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, request.DeviceId);

            return StatusCode(StatusCodes.Status408RequestTimeout,
                ApiErrorDetail.From(ex,
                    errorCode: "REQUEST_TIMEOUT",
                    requestId: requestId));
        }

        // ── Catch-all: unexpected server fault ────────────────────────────────
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception in POST /api/sync. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, request.DeviceId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrorDetail.From(ex,
                    errorCode: "INTERNAL_SERVER_ERROR",
                    requestId: requestId));
        }
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
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorDetail), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetServerChanges(
        [FromQuery] string deviceId,
        [FromQuery] DateTime since,
        [FromQuery] string? pageToken,
        CancellationToken ct)
    {
        var requestId = HttpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new ApiErrorDetail
            {
                ErrorCode  = "MISSING_PARAMETER",
                Message    = "Query parameter 'deviceId' is required.",
                RequestId  = requestId,
                Timestamp  = DateTime.UtcNow
            });
        }

        try
        {
            var (changes, nextToken) = await _syncService.GetServerChangesPageAsync(
                deviceId, since, pageToken, ct);

            return Ok(new
            {
                server_time     = DateTime.UtcNow,
                server_changes  = changes,
                next_page_token = nextToken
            });
        }

        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "GetServerChanges cancelled by client. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, deviceId);

            return StatusCode(499, ApiErrorDetail.From(ex,
                errorCode: "CLIENT_CLOSED_REQUEST",
                requestId: requestId));
        }

        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error in GetServerChanges. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, deviceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorDetail.From(ex,
                    errorCode: "DATABASE_ERROR",
                    requestId: requestId));
        }

        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error in GetServerChanges. RequestId={RequestId} DeviceId={DeviceId} SqlErrorNumber={Number}",
                requestId, deviceId, ex.Number);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorDetail.From(ex,
                    errorCode: $"SQL_ERROR_{ex.Number}",
                    requestId: requestId));
        }

        catch (TimeoutException ex)
        {
            _logger.LogError(ex,
                "Timeout in GetServerChanges. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, deviceId);

            return StatusCode(StatusCodes.Status408RequestTimeout,
                ApiErrorDetail.From(ex,
                    errorCode: "REQUEST_TIMEOUT",
                    requestId: requestId));
        }

        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception in GET /api/sync/changes. RequestId={RequestId} DeviceId={DeviceId}",
                requestId, deviceId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiErrorDetail.From(ex,
                    errorCode: "INTERNAL_SERVER_ERROR",
                    requestId: requestId));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true when a <see cref="DbUpdateException"/> wraps a SQL Server
    /// unique-constraint or primary-key violation (error numbers 2601 / 2627).
    /// Used to distinguish idempotency key conflicts from genuine DB faults.
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
        => ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
