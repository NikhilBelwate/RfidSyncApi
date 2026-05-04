using RfidSyncApi.Application.DTOs;
using RfidSyncApi.Domain.Entities;
using RfidSyncApi.Infrastructure.Repositories;

namespace RfidSyncApi.Application.Services;

/// <summary>
/// Computes CHECK_IN / CHECK_OUT session durations and bundles them per device.
///
/// Pairing algorithm (greedy, chronological):
///   For each (device_id, tag_id) group ordered by created_at ASC:
///     - CHECK_IN  → push onto an open-sessions stack.
///     - CHECK_OUT → if the stack has an open CHECK_IN, pop it and produce a
///                   COMPLETED session with computed duration.
///                 → if the stack is empty, produce an ORPHANED record
///                   (CHECK_OUT with no preceding CHECK_IN — data anomaly).
///   Any CHECK_INs still on the stack at the end are OPEN sessions.
///
/// This matches real-world RFID behaviour: a tag swiped IN before swipe OUT,
/// with occasional orphaned events from missed scans or clock skew.
/// </summary>
public class SessionService : ISessionService
{
    private readonly IRfidLogRepository _repo;
    private readonly ILogger<SessionService> _logger;

    public SessionService(IRfidLogRepository repo, ILogger<SessionService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<SessionsResponse> GetSessionsAsync(
        string? deviceId,
        CancellationToken ct = default)
    {
        var logs = await _repo.GetCheckInOutLogsAsync(deviceId, ct);

        _logger.LogInformation(
            "Sessions computation: loaded {Count} CHECK_IN/OUT records. DeviceFilter={DeviceFilter}",
            logs.Count, deviceId ?? "ALL");

        // Group by device, then within each device group by tag
        var deviceGroups = logs
            .GroupBy(l => l.DeviceId)
            .OrderBy(g => g.Key);

        var summaries = new List<DeviceSessionSummary>();

        foreach (var deviceGroup in deviceGroups)
        {
            var summary = BuildDeviceSummary(deviceGroup.Key, deviceGroup.ToList());
            summaries.Add(summary);
        }

        return new SessionsResponse
        {
            GeneratedAt  = DateTime.UtcNow,
            TotalDevices = summaries.Count,
            Devices      = summaries
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Core pairing logic
    // ══════════════════════════════════════════════════════════════════════════

    private static DeviceSessionSummary BuildDeviceSummary(
        string deviceId,
        List<RfidLog> deviceLogs)
    {
        var sessions = new List<SessionRecord>();

        // Group by tag_id — each physical tag is paired independently
        var tagGroups = deviceLogs
            .GroupBy(l => l.TagId)
            .OrderBy(g => g.Key);

        foreach (var tagGroup in tagGroups)
        {
            var tagSessions = PairEvents(tagGroup.OrderBy(l => l.CreatedAt).ToList());
            sessions.AddRange(tagSessions);
        }

        // Sort all sessions newest-first for the response
        sessions = sessions
            .OrderByDescending(s => s.CheckInTime ?? s.CheckOutTime)
            .ToList();

        // ── Aggregate stats ───────────────────────────────────────────────────
        var completed   = sessions.Where(s => s.Status == "COMPLETED").ToList();
        var durations   = completed.Select(s => s.DurationMinutes ?? 0).ToList();

        var totalDuration   = durations.Sum();
        var avgDuration     = durations.Count > 0 ? durations.Average() : (double?)null;
        var maxDuration     = durations.Count > 0 ? durations.Max()     : (double?)null;
        var minDuration     = durations.Count > 0 ? durations.Min()     : (double?)null;

        // Grab site_id from the most recent log (devices stay at one site normally)
        var siteId = deviceLogs
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.SiteId)
            .FirstOrDefault() ?? string.Empty;

        return new DeviceSessionSummary
        {
            DeviceId               = deviceId,
            SiteId                 = siteId,
            TotalSessions          = sessions.Count,
            CompletedSessions      = sessions.Count(s => s.Status == "COMPLETED"),
            OpenSessions           = sessions.Count(s => s.Status == "OPEN"),
            OrphanedCheckOuts      = sessions.Count(s => s.Status == "ORPHANED"),
            TotalDurationMinutes   = Math.Round(totalDuration, 2),
            AverageDurationMinutes = avgDuration.HasValue ? Math.Round(avgDuration.Value, 2) : null,
            MaxDurationMinutes     = maxDuration.HasValue ? Math.Round(maxDuration.Value, 2) : null,
            MinDurationMinutes     = minDuration.HasValue ? Math.Round(minDuration.Value, 2) : null,
            Sessions               = sessions
        };
    }

    /// <summary>
    /// Pairs CHECK_IN and CHECK_OUT events for a single (device, tag) combination.
    /// Events must be pre-sorted by created_at ASC.
    /// </summary>
    private static List<SessionRecord> PairEvents(List<RfidLog> events)
    {
        var sessions = new List<SessionRecord>();

        // Stack holds unmatched CHECK_INs (LIFO — closest CHECK_IN matches next CHECK_OUT)
        var openStack = new Stack<RfidLog>();

        foreach (var evt in events)
        {
            if (evt.EventType == "CHECK_IN")
            {
                openStack.Push(evt);
            }
            else // CHECK_OUT
            {
                if (openStack.Count > 0)
                {
                    // ── COMPLETED session ──────────────────────────────────
                    var checkIn  = openStack.Pop();
                    var duration = evt.CreatedAt - checkIn.CreatedAt;

                    sessions.Add(new SessionRecord
                    {
                        TagId              = evt.TagId,
                        UserId             = checkIn.UserId, // user who checked in
                        SiteId             = checkIn.SiteId,
                        CheckInServerId    = checkIn.ServerId,
                        CheckOutServerId   = evt.ServerId,
                        CheckInTime        = checkIn.CreatedAt,
                        CheckOutTime       = evt.CreatedAt,
                        DurationSeconds    = (long)duration.TotalSeconds,
                        DurationMinutes    = Math.Round(duration.TotalMinutes, 2),
                        DurationFormatted  = FormatDuration(duration),
                        Status             = "COMPLETED"
                    });
                }
                else
                {
                    // ── ORPHANED CHECK_OUT — no preceding CHECK_IN ─────────
                    sessions.Add(new SessionRecord
                    {
                        TagId            = evt.TagId,
                        UserId           = evt.UserId,
                        SiteId           = evt.SiteId,
                        CheckOutServerId = evt.ServerId,
                        CheckOutTime     = evt.CreatedAt,
                        Status           = "ORPHANED"
                    });
                }
            }
        }

        // ── Any remaining CHECK_INs on the stack are OPEN sessions ────────────
        foreach (var openCheckIn in openStack)
        {
            sessions.Add(new SessionRecord
            {
                TagId           = openCheckIn.TagId,
                UserId          = openCheckIn.UserId,
                SiteId          = openCheckIn.SiteId,
                CheckInServerId = openCheckIn.ServerId,
                CheckInTime     = openCheckIn.CreatedAt,
                Status          = "OPEN"
            });
        }

        return sessions;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Formats a TimeSpan as "Xd Xh Xm Xs", omitting leading zero components.
    /// Examples:  "45s"  |  "3m 12s"  |  "1h 5m 0s"  |  "2d 3h 0m 0s"
    /// </summary>
    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 0)
            return "0s";

        var parts = new List<string>();

        if (ts.Days    > 0) parts.Add($"{ts.Days}d");
        if (ts.Hours   > 0 || ts.Days > 0)  parts.Add($"{ts.Hours}h");
        if (ts.Minutes > 0 || ts.Hours > 0 || ts.Days > 0) parts.Add($"{ts.Minutes}m");
        parts.Add($"{ts.Seconds}s");

        return string.Join(" ", parts);
    }
}
