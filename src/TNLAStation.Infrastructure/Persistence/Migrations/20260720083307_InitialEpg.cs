using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEpg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    network_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_width_name = table.Column<string>(type: "text", nullable: false),
                    remote_control_key_id = table.Column<int>(type: "integer", nullable: true),
                    has_logo_data = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    channel_type_id = table.Column<int>(type: "integer", nullable: false),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                    table.UniqueConstraint("ak_channels_identity", x => new { x.id, x.network_id, x.service_id });
                    table.CheckConstraint("ck_channels_type", "channel_type IN ('GR', 'BS', 'CS', 'SKY')");
                    table.CheckConstraint("ck_channels_type_id", "channel_type_id = CASE channel_type WHEN 'GR' THEN 0 WHEN 'BS' THEN 1 WHEN 'CS' THEN 2 WHEN 'SKY' THEN 3 END");
                });

            migrationBuilder.CreateTable(
                name: "epg_sync_state",
                columns: table => new
                {
                    singleton_id = table.Column<short>(type: "smallint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    needs_full_sync = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_stream_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epg_sync_state", x => x.singleton_id);
                    table.CheckConstraint("ck_epg_sync_state_singleton", "singleton_id = 1");
                });

            migrationBuilder.CreateTable(
                name: "programs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    update_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    network_id = table.Column<int>(type: "integer", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_hour = table.Column<int>(type: "integer", nullable: false),
                    week = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    is_free = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_width_name = table.Column<string>(type: "text", nullable: false),
                    short_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    half_width_description = table.Column<string>(type: "text", nullable: true),
                    extended = table.Column<string>(type: "text", nullable: true),
                    half_width_extended = table.Column<string>(type: "text", nullable: true),
                    raw_extended = table.Column<string>(type: "json", nullable: true),
                    raw_half_width_extended = table.Column<string>(type: "json", nullable: true),
                    genre1 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre1 = table.Column<int>(type: "integer", nullable: true),
                    genre2 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre2 = table.Column<int>(type: "integer", nullable: true),
                    genre3 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre3 = table.Column<int>(type: "integer", nullable: true),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    video_type = table.Column<string>(type: "text", nullable: true),
                    video_resolution = table.Column<string>(type: "text", nullable: true),
                    video_stream_content = table.Column<int>(type: "integer", nullable: true),
                    video_component_type = table.Column<int>(type: "integer", nullable: true),
                    audio_sampling_rate = table.Column<int>(type: "integer", nullable: true),
                    audio_component_type = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programs", x => x.id);
                    table.CheckConstraint("ck_programs_start_hour", "start_hour BETWEEN 0 AND 23");
                    table.CheckConstraint("ck_programs_time", "end_at >= start_at AND duration_ms >= 0");
                    table.CheckConstraint("ck_programs_week", "week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_programs_channel",
                        columns: x => new { x.channel_id, x.network_id, x.service_id },
                        principalTable: "channels",
                        principalColumns: new[] { "id", "network_id", "service_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channels_display_order",
                table: "channels",
                columns: new[] { "channel_type_id", "remote_control_key_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "uq_channels_network_service",
                table: "channels",
                columns: new[] { "network_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_programs_channel_id_network_id_service_id",
                table: "programs",
                columns: new[] { "channel_id", "network_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_channel_start",
                table: "programs",
                columns: new[] { "channel_id", "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_end_at",
                table: "programs",
                column: "end_at");

            migrationBuilder.CreateIndex(
                name: "ix_programs_genre1",
                table: "programs",
                columns: new[] { "genre1", "sub_genre1" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_genre2",
                table: "programs",
                columns: new[] { "genre2", "sub_genre2" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_genre3",
                table: "programs",
                columns: new[] { "genre3", "sub_genre3" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_start_at",
                table: "programs",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "ix_programs_type_start",
                table: "programs",
                columns: new[] { "channel_type", "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "ix_programs_week_hour",
                table: "programs",
                columns: new[] { "week", "start_hour", "start_at" });

            migrationBuilder.CreateIndex(
                name: "uq_programs_event",
                table: "programs",
                columns: new[] { "network_id", "service_id", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "epg_sync_state");

            migrationBuilder.DropTable(
                name: "programs");

            migrationBuilder.DropTable(
                name: "channels");
        }
    }
}
