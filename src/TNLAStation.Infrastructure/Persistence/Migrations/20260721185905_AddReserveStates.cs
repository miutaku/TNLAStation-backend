using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TNLAStation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 「録らない」だけを持っていた表を、予約に対して人が示した意思をまとめて持つ表にする。
    ///
    /// 作り直すと、それまでに指定された除外がすべて消える。除外はもう一度指定しないと戻らず、
    /// 消えたことにも気づけないので、名前を替えて列を足す形にする。
    /// </summary>
    public partial class AddReserveStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "reserve_skips", newName: "reserve_states");

            // 既にある行はすべて「録らない」の指定だったので、真で埋めてから既定値を戻す。
            migrationBuilder.AddColumn<bool>(
                name: "is_skip",
                table: "reserve_states",
                type: "boolean",
                nullable: false,
                defaultValue: true);
            migrationBuilder.AlterColumn<bool>(
                name: "is_skip",
                table: "reserve_states",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_overlap_cleared",
                table: "reserve_states",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 重複の解除だけを持つ行は、戻した先に置き場がない。
            migrationBuilder.Sql("DELETE FROM reserve_states WHERE is_skip = false");
            migrationBuilder.DropColumn(name: "is_overlap_cleared", table: "reserve_states");
            migrationBuilder.DropColumn(name: "is_skip", table: "reserve_states");
            migrationBuilder.RenameTable(name: "reserve_states", newName: "reserve_skips");
        }
    }
}
