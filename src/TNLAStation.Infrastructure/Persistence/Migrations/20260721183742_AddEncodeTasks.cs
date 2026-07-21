using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEncodeTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "encode_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recorded_id = table.Column<long>(type: "bigint", nullable: false),
                    source_video_file_id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    parent_directory_name = table.Column<string>(type: "text", nullable: true),
                    directory = table.Column<string>(type: "text", nullable: true),
                    remove_original = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    percent = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_encode_tasks", x => x.id);
                    table.CheckConstraint("ck_encode_tasks_status", "status IN ('waiting', 'running')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_encode_tasks_queue",
                table: "encode_tasks",
                columns: new[] { "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_encode_tasks_recorded",
                table: "encode_tasks",
                column: "recorded_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encode_tasks");
        }
    }
}
