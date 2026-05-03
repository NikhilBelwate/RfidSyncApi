using System.Text.Json.Serialization;

namespace RfidSyncApi.Application.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
//  OUTBOUND  –  GET /api/logs   response contract
// ══════════════════════════════════════════════════════════════════════════════

public class LogsResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("items")]
    public List<LogItem> Items { get; set; } = new();
}

public class LogItem
{
    [JsonPropertyName("server_id")]
    public Guid ServerId { get; set; }

    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("local_id")]
    public string LocalId { get; set; } = string.Empty;

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
