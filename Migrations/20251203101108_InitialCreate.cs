using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicViewer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comics",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(32)", maxLength: 32, nullable: false, comment: "MD5主键"),
                    Title = table.Column<string>(type: "TEXT", nullable: true, comment: "漫画标题"),
                    CreatedTime = table.Column<DateTime>(type: "DATETIME", nullable: true, comment: "创建时间"),
                    LastAccess = table.Column<DateTime>(type: "DATETIME", nullable: true, comment: "最后访问时间"),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0, comment: "阅读进度"),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0, comment: "评分 0-5")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comics", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(32)", maxLength: 32, nullable: false, comment: "MD5主键"),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MovingFiles",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Src = table.Column<string>(type: "TEXT", nullable: false),
                    Dst = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovingFiles", x => x.Key);
                    table.ForeignKey(
                        name: "FK_MovingFiles_Comics_Key",
                        column: x => x.Key,
                        principalTable: "Comics",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComicTags",
                columns: table => new
                {
                    ComicKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, comment: "漫画MD5外键"),
                    TagKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, comment: "标签MD5外键")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicTags", x => new { x.ComicKey, x.TagKey });
                    table.ForeignKey(
                        name: "FK_ComicTags_Comics_ComicKey",
                        column: x => x.ComicKey,
                        principalTable: "Comics",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComicTags_Tags_TagKey",
                        column: x => x.TagKey,
                        principalTable: "Tags",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComicTags_ComicKey",
                table: "ComicTags",
                column: "ComicKey");

            migrationBuilder.CreateIndex(
                name: "IX_ComicTags_TagKey",
                table: "ComicTags",
                column: "TagKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComicTags");

            migrationBuilder.DropTable(
                name: "MovingFiles");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Comics");
        }
    }
}
