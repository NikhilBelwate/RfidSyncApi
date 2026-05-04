namespace RfidSyncApi.Application.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
//  Sessions API — DTOs
//  GET /api/sessions
//  GET /api/sessions?device_id={deviceId}
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Top-level response envelope for the sessions endpoint.
/// </summary>
public class SessionsResponse
{
    /// <summary>UTC timestamp when this response was generated.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of distinct devices included in this response.</summary>
    public int TotalDevices { get; set; }

    /// <summary>Per-device session summaries.</summary>
    public List<DeviceSessionSummary> Devices { get; set; } = new();
}

/// <summary>
/// All CHECK_IN / CHECK_OUT sessions for a single device, with aggregate stats.
/// </summary>
public class DeviceSessionSummary
{
    /// <summary>Device identifier.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Site the device belongs to (from the most recent log entry).</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Total number of sessions (completed + open + orphaned).</summary>
    public int TotalSessions { get; set; }

    /// <summary>Sessions where both CHECK_IN and CHECK_OUT were recorded.</summary>
    public int CompletedSessions { get; set; }

    /// <summary>Sessions where CHECK_IN was recorded but no matching CHECK_OUT yet.</summary>
    public int OpenSessions { get; set; }

    /// <summary>CHECK_OUT events with no preceding CHECK_IN (data anomaly).</summary>
    public int OrphanedCheckOuts { get; set; }

    /// <summary>Sum of all completed session durations in minutes.</summary>
    public double TotalDurationMinutes { get; set; }

    /// <summary>Average session duration in minutes across completed sessions only.</summary>
    public double? AverageDurationMinutes { get; set; }

    /// <summary>Longest completed session duration in minutes.</summary>
    public double? MaxDurationMinutes { get; set; }

    /// <summary>Shortest completed session duration in minutes.</summary>
    public double? MinDurationMinutes { get; set; }

    /// <summary>Ordered list of individual sessions (newest first).</summary>
    public List<SessionRecord> Sessions { get; set; } = new();
}

/// <summary>
/// A single paired CHECK_IN → CHECK_OUT session (or an open / orphaned event).
/// </summary>
public class SessionRecord
{
    /// <summary>Physical RFID tag ID that was scanned.</summary>
    public string TagId { get; set; } = string.Empty;

    /// <summary>User who performed the scans.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Site where the session took place.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Server-assigned ID of the CHECK_IN log record.</summary>
    public Guid? CheckInServerId { get; set; }

    /// <summary>Server-assigned ID of the CHECK_OUT log record.</summary>
    public Guid? CheckOutServerId { get; set; }

    /// <summary>UTC timestamp of the CHECK_IN scan.</summary>
    public DateTime? CheckInTime { get; set; }

    /// <summary>UTC timestamp of the CHECK_OUT scan.</summary>
    public DateTime? CheckOutTime { get; set; }

    /// <summary>Total session duration in whole seconds.</summary>
    public long? DurationSeconds { get; set; }

    /// <summary>Session duration in fractional minutes (rounded to 2 dp).</summary>
    public double? DurationMinutes { get; set; }

    /// <summary>Human-readable duration string, e.g. "1h 23m 45s".</summary>
    public string? DurationFormatted { get; set; }

    /// <summary>
    /// Session lifecycle status:
    ///   COMPLETED    — both CHECK_IN and CHECK_OUT recorded.
    ///   OPEN         — CHECK_IN recorded, no matching CHECK_OUT yet.
    ///   ORPHANED     — CHECK_OUT with no preceding CHECK_IN (data anomaly).
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
