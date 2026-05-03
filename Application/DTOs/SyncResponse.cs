using System.Text.Json.Serialization;

namespace RfidSyncApi.Application.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
//  OUTBOUND  –  POST /api/sync   response contract
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Full sync response:
///  • per-record results for every change the client sent
///  • delta of server-side changes the client hasn't seen yet
/// </summary>
public class SyncResponse
{
    /// <summary>Server UTC timestamp — client should store this as its next last_sync_time.</summary>
    [JsonPropertyName("server_time")]
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;

    /// <summary>One entry per incoming ChangeRecord.</summary>
    [JsonPropertyName("results")]
    public List<ChangeResult> Results { get; set; } = new();

    /// <summary>Records modified on the server that the device hasn't seen yet.</summary>
    [JsonPropertyName("server_changes")]
    public List<ServerChange> ServerChanges { get; set; } = new();

    /// <summary>Pagination cursor for the next server_changes page (null = no more pages).</summary>
    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}

/// <summary>Result for one client change record.</summary>
public class ChangeResult
{
    [JsonPropertyName("local_id")]
    public string LocalId { get; set; } = string.Empty;

    /// <summary>Server-assigned ID (populated on SUCCESS / CONFLICT_SERVER_WINS).</summary>
    [JsonPropertyName("server_id")]
    public Guid? ServerId { get; set; }

    /// <summary>SUCCESS | CONFLICT_CLIENT_WINS | CONFLICT_SERVER_WINS | SKIPPED | ERROR</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Human-readable reason for non-SUCCESS outcomes.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Current server record when client is behind (CONFLICT_SERVER_WINS).</summary>
    [JsonPropertyName("server_data")]
    public ServerChangeData? ServerData { get; set; }
}

/// <summary>Server-initiated change pushed to the client during sync.</summary>
public class ServerChange
{
    [JsonPropertyName("server_id")]
    public Guid ServerId { get; set; }

    /// <summary>INSERT | UPDATE | DELETE</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public ServerChangeData Data { get; set; } = new();
}

/// <summary>Shape of data returned from the server to the device.</summary>
public class ServerChangeData
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
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; set; }
}
