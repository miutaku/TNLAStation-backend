using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecorded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recorded",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<long>(type: "bigint", nullable: true),
                    rule_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_width_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    half_width_description = table.Column<string>(type: "text", nullable: true),
                    extended = table.Column<string>(type: "text", nullable: true),
                    half_width_extended = table.Column<string>(type: "text", nullable: true),
                    genre1 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre1 = table.Column<int>(type: "integer", nullable: true),
                    genre2 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre2 = table.Column<int>(type: "integer", nullable: true),
                    genre3 = table.Column<int>(type: "integer", nullable: true),
                    sub_genre3 = table.Column<int>(type: "integer", nullable: true),
                    is_recording = table.Column<bool>(type: "boolean", nullable: false),
                    is_protected = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recorded", x => x.id);
                    table.CheckConstraint("ck_recorded_time", "end_at >= start_at");
                });

            migrationBuilder.CreateTable(
                name: "recorded_tags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recorded_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "video_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recorded_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    filename = table.Column<string>(type: "text", nullable: false),
                    parent_directory_name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_files", x => x.id);
                    table.CheckConstraint("ck_video_files_type", "type IN ('ts', 'encoded')");
                    table.ForeignKey(
                        name: "fk_video_files_recorded",
                        column: x => x.recorded_id,
                        principalTable: "recorded",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recorded_tag_links",
                columns: table => new
                {
                    recorded_id = table.Column<long>(type: "bigint", nullable: false),
                    tag_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recorded_tag_links", x => new { x.recorded_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_recorded_tag_links_recorded",
                        column: x => x.recorded_id,
                        principalTable: "recorded",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recorded_tag_links_tag",
                        column: x => x.tag_id,
                        principalTable: "recorded_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recorded_channel",
                table: "recorded",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_recorded_is_recording",
                table: "recorded",
                column: "is_recording");

            migrationBuilder.CreateIndex(
                name: "ix_recorded_rule",
                table: "recorded",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_recorded_start_at",
                table: "recorded",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "uq_recorded_program",
                table: "recorded",
                column: "program_id",
                unique: true,
                filter: "program_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recorded_tag_links_tag",
                table: "recorded_tag_links",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "uq_recorded_tags_name",
                table: "recorded_tags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_video_files_recorded",
                table: "video_files",
                column: "recorded_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recorded_tag_links");

            migrationBuilder.DropTable(
                name: "video_files");

            migrationBuilder.DropTable(
                name: "recorded_tags");

            migrationBuilder.DropTable(
                name: "recorded");
        }
    }
}
