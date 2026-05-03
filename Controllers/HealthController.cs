using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RfidSyncApi.Infrastructure.Persistence;

namespace RfidSyncApi.Controllers;

/// <summary>
/// GET /health — Azure App Service health probe endpoint.
/// Checks database connectivity and returns 200/503.
/// Does NOT require the API token (excluded in middleware).
/// </summary>
[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(ApplicationDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            // Lightweight connectivity check — CanConnectAsync doesn't run a full query
            var canConnect = await _db.Database.CanConnectAsync(ct);
            if (!canConnect)
                return StatusCode(503, new { status = "unhealthy", db = "cannot connect" });

            return Ok(new
            {
                status = "healthy",
                db = "connected",
                utc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
        }
    }
}
