using Microsoft.AspNetCore.Mvc;
using RfidSyncApi.Application.Services;

namespace RfidSyncApi.Controllers;

/// <summary>
/// GET /api/logs — read-only query endpoint for debugging, auditing, and demo dashboards.
/// All heavy lifting delegated to SyncService.GetLogsAsync.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LogsController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly ILogger<LogsController> _logger;

    public LogsController(ISyncService syncService, ILogger<LogsController> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GET /api/logs
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a paginated, filtered list of RFID scan logs.
    /// </summary>
    /// <param name="siteId">Filter by site identifier (optional).</param>
    /// <param name="from">UTC start date (inclusive). Defaults to last 24 h if both null.</param>
    /// <param name="to">UTC end date (inclusive).</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Records per page, max 500 (default 50).</param>
    [HttpGet]
    [ProducesResponseType(typeof(Application.DTOs.LogsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? siteId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) return BadRequest(new { error = "page must be >= 1." });
        if (pageSize is < 1 or > 500)
            return BadRequest(new { error = "pageSize must be between 1 and 500." });

        if (from.HasValue && to.HasValue && from > to)
            return BadRequest(new { error = "from must be before to." });

        // Default time window: last 24 h when neither bound is supplied
        var effectiveFrom = from ?? (to.HasValue ? null : (DateTime?)DateTime.UtcNow.AddDays(-1));
        var effectiveTo = to;

        _logger.LogInformation(
            "Logs query. SiteId={SiteId} From={From} To={To} Page={Page} PageSize={PageSize}",
            siteId, effectiveFrom, effectiveTo, page, pageSize);

        var result = await _syncService.GetLogsAsync(
            siteId, effectiveFrom, effectiveTo, page, pageSize, ct);

        return Ok(result);
    }
}
