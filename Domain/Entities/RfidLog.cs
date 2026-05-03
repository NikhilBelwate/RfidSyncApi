using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RfidSyncApi.Domain.Entities;

/// <summary>
/// Core RFID scan log entity — every scan event from every device lands here.
/// The version + updated_at pair drives the conflict-resolution strategy.
/// </summary>
[Table("rfid_logs")]
public class RfidLog
{
    /// <summary>Server-assigned surrogate PK (GUID). Never exposed to the client as a mutable field.</summary>
    [Key]
    [Column("server_id")]
    public Guid ServerId { get; set; } = Guid.NewGuid();

    /// <summary>Originating Android device identifier.</summary>
    [Required]
    [MaxLength(128)]
    [Column("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Client-side UUID so we can correlate INSERT responses back to the caller.</summary>
    [Required]
    [MaxLength(128)]
    [Column("local_id")]
    public string LocalId { get; set; } = string.Empty;

    /// <summary>Physical RFID tag identifier. Regex-validated at the API layer.</summary>
    [Required]
    [MaxLength(64)]
    [Column("tag_id")]
    public string TagId { get; set; } = string.Empty;

    /// <summary>User who performed the scan.</summary>
    [Required]
    [MaxLength(128)]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Industrial site / location identifier — also used as a partition hint.</summary>
    [Required]
    [MaxLength(128)]
    [Column("site_id")]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Domain event type (CHECK_IN, CHECK_OUT, INSPECTION, …).</summary>
    [Required]
    [MaxLength(64)]
    [Column("event_type")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>When the scan physically happened on the device (device clock).</summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last mutation timestamp — participates in last-write-wins conflict resolution.</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic-concurrency counter.
    /// Incoming &gt; stored  → accept.
    /// Incoming &lt; stored  → reject.
    /// Equal               → fall back to updated_at (last-write-wins).
    /// </summary>
    [Column("version")]
    public int Version { get; set; } = 1;

    /// <summary>Soft-delete flag — never hard-delete RFID audit records.</summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; } = false;
}
