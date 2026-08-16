using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFolders_MediaFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FolderId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MediaKind = table.Column<byte>(type: "tinyint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    AltText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDecorative = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Credit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FocalPointX = table.Column<double>(type: "float", nullable: true),
                    FocalPointY = table.Column<double>(type: "float", nullable: true),
                    EditsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditsVersion = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaRenditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MediaItemId = table.Column<int>(type: "int", nullable: false),
                    SpecHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Spec = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Quality = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EditsVersion = table.Column<int>(type: "int", nullable: false),
                    GeneratedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    LastAccessedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRenditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRenditions_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_ParentId_SortOrder_Live",
                table: "MediaFolders",
                columns: new[] { "ParentId", "SortOrder" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_Path",
                table: "MediaFolders",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_FolderId",
                table: "MediaItems",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_MediaKind_IsDeleted",
                table: "MediaItems",
                columns: new[] { "MediaKind", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_Sha256_Live",
                table: "MediaItems",
                column: "Sha256",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRenditions_MediaItemId_SpecHash",
                table: "MediaRenditions",
                columns: new[] { "MediaItemId", "SpecHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaRenditions");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "MediaFolders");
        }
    }
}
