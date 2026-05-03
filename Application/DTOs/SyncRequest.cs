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
    /// UTC timestamp of the device's last successful sync.
    /// The server returns all records modified after this time in server_changes.
    /// </summary>
    [JsonPropertyName("last_sync_time")]
    public DateTime? LastSyncTime { get; set; }

    /// <summary>Batch of changes to apply. Max 10 000 items.</summary>
    [Required]
    [JsonPropertyName("changes")]
    public List<ChangeRecord> Changes { get; set; } = new();
}

/// <summary>One INSERT / UPDATE / DELETE event from the device.</summary>
public class ChangeRecord
{
    /// <summary>Client-generated UUID — returned in the response for correlation.</summary>
    [Required]
    [JsonPropertyName("local_id")]
    public string LocalId { get; set; } = string.Empty;

    /// <summary>
    /// Server-assigned ID when the device already knows it
    /// (UPDATE / DELETE flows).  Null for first-time INSERT.
    /// </summary>
    [JsonPropertyName("server_id")]
    public Guid? ServerId { get; set; }

    /// <summary>INSERT | UPDATE | DELETE</summary>
    [Required]
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    /// <summary>Payload fields for INSERT / UPDATE (null is acceptable for soft-DELETE).</summary>
    [JsonPropertyName("data")]
    public ChangeData? Data { get; set; }
}

/// <summary>Mutable fields the client can push.</summary>
public class ChangeData
{
    [JsonPropertyName("tag_id")]
    public string TagId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("site_id")]
    public string SiteId { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
}
