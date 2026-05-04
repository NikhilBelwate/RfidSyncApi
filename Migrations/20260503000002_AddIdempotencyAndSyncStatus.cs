using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RfidSyncApi.Migrations;

/// <inheritdoc />
public partial class AddIdempotencyAndSyncStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── 1. Add idempotency_key column ─────────────────────────────────────
        //    SHA-256 hex of: device_id|local_id|tag_id|event_type|user_id|created_at_ms
        //    Backfill existing rows with a placeholder so NOT NULL can be enforced.
        migrationBuilder.AddColumn<string>(
            name: "idempotency_key",
            table: "rfid_logs",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");   // placeholder — rows pre-dating this migration get ""

        // ── 2. Unique index on idempotency_key ────────────────────────────────
        //    Note: existing seed rows all have "" — they are excluded from the
        //    uniqueness check via a filtered index so the migration doesn't fail.
        migrationBuilder.Sql(@"
            CREATE UNIQUE INDEX UX_rfid_logs_idempotency_key
            ON rfid_logs (idempotency_key)
            WHERE idempotency_key <> '';
        ");

        // ── 3. Add sync_status column (nullable — informational only) ─────────
        migrationBuilder.AddColumn<string>(
            name: "sync_status",
            table: "rfid_logs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS UX_rfid_logs_idempotency_key ON rfid_logs;");

        migrationBuilder.DropColumn(
            name: "idempotency_key",
            table: "rfid_logs");

        migrationBuilder.DropColumn(
            name: "sync_status",
            table: "rfid_logs");
    }
}
