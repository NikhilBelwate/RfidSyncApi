using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RfidSyncApi.Application.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
//  INBOUND  –  POST /api/sync   request contract
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Top-level sync request from an Android device.
/// Carries up to 10 000 change records in a single call.
/// </summary>
public class SyncRequest
{
    /// <summary>Unique device identifier (e.g. Android ID or fleet asset tag).</summary>
    [Required]
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Unix epoch milliseconds of the device's last successful sync.
    /// Use 0 to indicate "no previous sync" (returns no server_changes delta).
    /// </summary>
    [JsonPropertyName("last_sync_time")]
    public long LastSyncTime { get; set; } = 0;

    /// <summary>Batch of changes to apply. Max 10 000 items.</summary>
    [Required]
    [JsonPropertyName("changes")]
    public List<ChangeRecord> Changes { get; set; } = new();
}

/// <summary>
/// One change record from the device — all scan fields are flat (no nested data wrapper).
/// <para>
/// <b>operation</b> is optional; when omitted the server treats the record as an INSERT.
/// Valid values: INSERT | UPDATE | DELETE.
/// </para>
/// </summary>
public class ChangeRecord
{
    /// <summary>Client-generated UUID — echoed back in the response for correlation.</summary>
    [Required]
    [JsonPropertyName("local_id")]
    public string LocalId { get; set; } = string.Empty;

    /// <summary>
    /// Server-assigned ID.  Required only for UPDATE / DELETE flows where the
    /// device already knows the server record.  Null / omitted for first-time INSERTs.
    /// </summary>
    [JsonPropertyName("server_id")]
    public Guid? ServerId { get; set; }

    /// <summary>
    /// INSERT | UPDATE | DELETE.
    /// Optional — defaults to INSERT when absent or null.
    /// </summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    /// <summary>
    /// Resolved operation: returns the explicit <see cref="Operation"/> value if
    /// provided, otherwise "INSERT".
    /// </summary>
    [JsonIgnore]
    public string EffectiveOperation =>
        string.IsNullOrWhiteSpace(Operation) ? "INSERT" : Operation.ToUpperInvariant();

    // ── Scan payload fields (flat — no nested "data" object) ─────────────────

    [JsonPropertyName("tag_id")]
    public string TagId { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("site_id")]
    public string? SiteId { get; set; }

    /// <summary>Unix epoch milliseconds when the tag was scanned on the device.</summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    /// <summary>Unix epoch milliseconds of the last local modification.</summary>
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Client-side sync status (e.g. PENDING, SYNCED). Informational only.</summary>
    [JsonPropertyName("sync_status")]
    public string? SyncStatus { get; set; }
}
