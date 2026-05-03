using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RfidSyncApi.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rfid_logs",
            columns: table => new
            {
                server_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                device_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                local_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                tag_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                user_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                site_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                event_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rfid_logs", x => x.server_id);
            });

        // ── Unique index: one record per (device, local) pair ────────────────
        migrationBuilder.CreateIndex(
            name: "UX_rfid_logs_device_local",
            table: "rfid_logs",
            columns: new[] { "device_id", "local_id" },
            unique: true);

        // ── Query indexes ────────────────────────────────────────────────────
        migrationBuilder.CreateIndex(
            name: "IX_rfid_logs_device_id",
            table: "rfid_logs",
            column: "device_id");

        migrationBuilder.CreateIndex(
            name: "IX_rfid_logs_updated_at",
            table: "rfid_logs",
            column: "updated_at");

        migrationBuilder.CreateIndex(
            name: "IX_rfid_logs_tag_id",
            table: "rfid_logs",
            column: "tag_id");

        migrationBuilder.CreateIndex(
            name: "IX_rfid_logs_device_updated",
            table: "rfid_logs",
            columns: new[] { "device_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_rfid_logs_site_id",
            table: "rfid_logs",
            column: "site_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "rfid_logs");
    }
}
