using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    update_cnt = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    is_time_specification = table.Column<bool>(type: "boolean", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: true),
                    half_width_keyword = table.Column<string>(type: "text", nullable: true),
                    ignore_keyword = table.Column<string>(type: "text", nullable: true),
                    half_width_ignore_keyword = table.Column<string>(type: "text", nullable: true),
                    key_cs = table.Column<bool>(type: "boolean", nullable: false),
                    key_reg_exp = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<bool>(type: "boolean", nullable: false),
                    extended = table.Column<bool>(type: "boolean", nullable: false),
                    ignore_key_cs = table.Column<bool>(type: "boolean", nullable: false),
                    ignore_key_reg_exp = table.Column<bool>(type: "boolean", nullable: false),
                    ignore_name = table.Column<bool>(type: "boolean", nullable: false),
                    ignore_description = table.Column<bool>(type: "boolean", nullable: false),
                    ignore_extended = table.Column<bool>(type: "boolean", nullable: false),
                    gr = table.Column<bool>(type: "boolean", nullable: false),
                    bs = table.Column<bool>(type: "boolean", nullable: false),
                    cs = table.Column<bool>(type: "boolean", nullable: false),
                    sky = table.Column<bool>(type: "boolean", nullable: false),
                    channel_ids = table.Column<string>(type: "json", nullable: true),
                    genres = table.Column<string>(type: "json", nullable: true),
                    times = table.Column<string>(type: "json", nullable: true),
                    is_free = table.Column<bool>(type: "boolean", nullable: false),
                    duration_min = table.Column<int>(type: "integer", nullable: true),
                    duration_max = table.Column<int>(type: "integer", nullable: true),
                    search_periods = table.Column<string>(type: "json", nullable: true),
                    enable = table.Column<bool>(type: "boolean", nullable: false),
                    avoid_duplicate = table.Column<bool>(type: "boolean", nullable: false),
                    period_to_avoid_duplicate = table.Column<int>(type: "integer", nullable: true),
                    allow_end_lack = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    is_delete_original_after_encode = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rules_enable",
                table: "rules",
                column: "enable");

            migrationBuilder.CreateIndex(
                name: "ix_rules_half_width_keyword",
                table: "rules",
                column: "half_width_keyword");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rules");
        }
    }
}
