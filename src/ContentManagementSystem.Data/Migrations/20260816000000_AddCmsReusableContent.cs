using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsReusableContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReusableContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FolderId = table.Column<int>(type: "int", nullable: true),
                    BlockTypeId = table.Column<int>(type: "int", nullable: false),
                    DraftVersionId = table.Column<int>(type: "int", nullable: true),
                    PublishedVersionId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_ReusableContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReusableContents_BlockTypes_BlockTypeId",
                        column: x => x.BlockTypeId,
                        principalTable: "BlockTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReusableContentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReusableContentId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockTypeRevision = table.Column<int>(type: "int", nullable: false),
                    PublishOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    UnpublishOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    PublishedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    PublishedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReusableContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReusableContentVersions_ReusableContents_ReusableContentId",
                        column: x => x.ReusableContentId,
                        principalTable: "ReusableContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContents_BlockTypeId",
                table: "ReusableContents",
                column: "BlockTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContents_DraftVersionId",
                table: "ReusableContents",
                column: "DraftVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContents_FolderId_Name_Live",
                table: "ReusableContents",
                columns: new[] { "FolderId", "Name" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContents_Key",
                table: "ReusableContents",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContents_PublishedVersionId",
                table: "ReusableContents",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContentVersions_ReusableContentId_Status",
                table: "ReusableContentVersions",
                columns: new[] { "ReusableContentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContentVersions_ReusableContentId_VersionNumber",
                table: "ReusableContentVersions",
                columns: new[] { "ReusableContentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableContentVersions_Status_PublishOn",
                table: "ReusableContentVersions",
                columns: new[] { "Status", "PublishOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReusableContents_ReusableContentVersions_DraftVersionId",
                table: "ReusableContents",
                column: "DraftVersionId",
                principalTable: "ReusableContentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReusableContents_ReusableContentVersions_PublishedVersionId",
                table: "ReusableContents",
                column: "PublishedVersionId",
                principalTable: "ReusableContentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReusableContents_ReusableContentVersions_DraftVersionId",
                table: "ReusableContents");

            migrationBuilder.DropForeignKey(
                name: "FK_ReusableContents_ReusableContentVersions_PublishedVersionId",
                table: "ReusableContents");

            migrationBuilder.DropTable(
                name: "ReusableContentVersions");

            migrationBuilder.DropTable(
                name: "ReusableContents");
        }
    }
}
