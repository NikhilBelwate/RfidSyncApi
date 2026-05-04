using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RfidSyncApi.Domain.Entities;

/// <summary>
/// Core RFID scan log entity — every scan event from every device lands here.
///
/// Idempotency:
///   • <see cref="DeviceId"/> + <see cref="LocalId"/> is a DB-level UNIQUE constraint
///     (one logical record per device origin).
///   • <see cref="IdempotencyKey"/> is a SHA-256 hash of the five business-key fields
///     plus the client timestamp.  A second UNIQUE constraint on this column ensures
///     that concurrent duplicate submissions (same physical event submitted twice in a
///     race) are caught at the DB layer even if the in-memory check was bypassed.
///
/// Conflict resolution:
///   The <see cref="Version"/> + <see cref="UpdatedAt"/> pair drives last-write-wins.
/// </summary>
[Table("rfid_logs")]
public class RfidLog
{
    /// <summary>Server-assigned surrogate PK (GUID). Never exposed as a mutable field.</summary>
    [Key]
    [Column("server_id")]
    public Guid ServerId { get; set; } = Guid.NewGuid();

    /// <summary>Originating Android device identifier.</summary>
    [Required]
    [MaxLength(128)]
    [Column("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Client-generated UUID — correlates INSERT responses back to the caller.</summary>
    [Required]
    [MaxLength(128)]
    [Column("local_id")]
    public string LocalId { get; set; } = string.Empty;

    /// <summary>Physical RFID tag identifier.</summary>
    [Required]
    [MaxLength(64)]
    [Column("tag_id")]
    public string TagId { get; set; } = string.Empty;

    /// <summary>User who performed the scan.</summary>
    [Required]
    [MaxLength(128)]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Industrial site / location identifier.</summary>
    [Required]
    [MaxLength(128)]
    [Column("site_id")]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Domain event type (CHECK_IN, CHECK_OUT, SCAN, …).</summary>
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
    ///   Incoming &gt; stored  → accept.
    ///   Incoming &lt; stored  → reject.
    ///   Equal               → fall back to updated_at (last-write-wins).
    /// </summary>
    [Column("version")]
    public int Version { get; set; } = 1;

    /// <summary>Soft-delete flag — never hard-delete RFID audit records.</summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; } = false;

    // ── Idempotency ───────────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 hex digest of: device_id|local_id|tag_id|event_type|user_id|created_at_epoch_ms.
    /// Stored as a 64-char lowercase hex string.
    /// A UNIQUE DB index on this column provides a second safety net against concurrent
    /// duplicate submissions that slip past the in-memory idempotency check.
    /// </summary>
    [MaxLength(64)]
    [Column("idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    // ── Client sync metadata ──────────────────────────────────────────────────

    /// <summary>
    /// Client-reported sync status at the time of submission.
    /// Values: PENDING | SYNCED | FAILED.
    /// Informational only — not used in server-side conflict resolution.
    /// </summary>
    [MaxLength(16)]
    [Column("sync_status")]
    public string? SyncStatus { get; set; }
}
