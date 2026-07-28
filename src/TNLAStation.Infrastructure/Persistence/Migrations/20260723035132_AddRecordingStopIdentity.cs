using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingStopIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "manual_reserve_id",
                table: "recorded",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reserve_id",
                table: "recorded",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reserve_key",
                table: "recorded",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_recorded_reserve_key",
                table: "recorded",
                column: "reserve_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recorded_reserve_key",
                table: "recorded");

            migrationBuilder.DropColumn(
                name: "manual_reserve_id",
                table: "recorded");

            migrationBuilder.DropColumn(
                name: "reserve_id",
                table: "recorded");

            migrationBuilder.DropColumn(
                name: "reserve_key",
                table: "recorded");
        }
    }
}
