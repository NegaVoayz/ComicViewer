using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicViewer.Migrations
{
    /// <inheritdoc />
    public partial class Add_Source : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "LastAccess",
                table: "Comics",
                type: "DATETIME",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                comment: "最后访问时间",
                oldClrType: typeof(DateTime),
                oldType: "DATETIME",
                oldNullable: true,
                oldComment: "最后访问时间");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedTime",
                table: "Comics",
                type: "DATETIME",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "DATETIME",
                oldNullable: true,
                oldComment: "创建时间");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Comics",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                comment: "漫画源");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Comics");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastAccess",
                table: "Comics",
                type: "DATETIME",
                nullable: true,
                comment: "最后访问时间",
                oldClrType: typeof(DateTime),
                oldType: "DATETIME",
                oldComment: "最后访问时间");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedTime",
                table: "Comics",
                type: "DATETIME",
                nullable: true,
                comment: "创建时间",
                oldClrType: typeof(DateTime),
                oldType: "DATETIME",
                oldComment: "创建时间");
        }
    }
}
