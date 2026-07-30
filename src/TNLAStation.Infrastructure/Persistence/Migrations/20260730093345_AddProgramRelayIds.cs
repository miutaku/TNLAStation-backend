using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramRelayIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "relay_program_ids",
                table: "programs",
                type: "json",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "relay_program_ids",
                table: "programs");
        }
    }
}
