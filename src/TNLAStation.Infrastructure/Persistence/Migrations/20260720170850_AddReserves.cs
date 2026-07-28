using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReserves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_reserves",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<long>(type: "bigint", nullable: true),
                    is_time_specified = table.Column<bool>(type: "boolean", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_width_name = table.Column<string>(type: "text", nullable: false),
                    allow_end_lack = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_delete_original_after_encode = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<string>(type: "json", nullable: true),
                    parent_directory_name = table.Column<string>(type: "text", nullable: true),
                    directory = table.Column<string>(type: "text", nullable: true),
                    recorded_format = table.Column<string>(type: "text", nullable: true),
                    mode1 = table.Column<string>(type: "text", nullable: true),
                    parent_directory_name1 = table.Column<string>(type: "text", nullable: true),
                    directory1 = table.Column<string>(type: "text", nullable: true),
                    mode2 = table.Column<string>(type: "text", nullable: true),
                    parent_directory_name2 = table.Column<string>(type: "text", nullable: true),
                    directory2 = table.Column<string>(type: "text", nullable: true),
                    mode3 = table.Column<string>(type: "text", nullable: true),
                    parent_directory_name3 = table.Column<string>(type: "text", nullable: true),
                    directory3 = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_reserves", x => x.id);
                    table.CheckConstraint("ck_manual_reserves_target", "program_id IS NOT NULL OR is_time_specified");
                    table.CheckConstraint("ck_manual_reserves_time", "end_at > start_at");
                });

            migrationBuilder.CreateTable(
                name: "reserve_skips",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserve_skips", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "reserves",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    rule_id = table.Column<long>(type: "bigint", nullable: true),
                    program_id = table.Column<long>(type: "bigint", nullable: true),
                    manual_reserve_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_width_name = table.Column<string>(type: "text", nullable: false),
                    is_skip = table.Column<bool>(type: "boolean", nullable: false),
                    is_conflict = table.Column<bool>(type: "boolean", nullable: false),
                    is_overlap = table.Column<bool>(type: "boolean", nullable: false),
                    tuner_index = table.Column<int>(type: "integer", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserves", x => x.id);
                    table.CheckConstraint("ck_reserves_time", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_reserves_manual_reserve",
                        column: x => x.manual_reserve_id,
                        principalTable: "manual_reserves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manual_reserves_start_at",
                table: "manual_reserves",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "uq_manual_reserves_program",
                table: "manual_reserves",
                column: "program_id",
                unique: true,
                filter: "program_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reserves_manual_reserve_id",
                table: "reserves",
                column: "manual_reserve_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserves_rule",
                table: "reserves",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserves_start_at",
                table: "reserves",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "uq_reserves_key",
                table: "reserves",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reserve_skips");

            migrationBuilder.DropTable(
                name: "reserves");

            migrationBuilder.DropTable(
                name: "manual_reserves");
        }
    }
}
