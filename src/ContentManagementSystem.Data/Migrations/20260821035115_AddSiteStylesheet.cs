using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteStylesheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteStylesheetRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteStylesheetId = table.Column<int>(type: "int", nullable: false),
                    Css = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ByteLength = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteStylesheetRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteStylesheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DraftCss = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedCss = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    PublishedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true),
                    PublishedRevisionId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteStylesheets", x => x.Id);
                    table.CheckConstraint("CK_SiteStylesheet_SingleRow", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_SiteStylesheets_SiteStylesheetRevisions_PublishedRevisionId",
                        column: x => x.PublishedRevisionId,
                        principalTable: "SiteStylesheetRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SiteStylesheets",
                columns: new[] { "Id", "CreatedBy", "CreatedOn", "DraftCss", "ModifiedBy", "ModifiedOn", "PublishedByUserId", "PublishedCss", "PublishedHash", "PublishedOn", "PublishedRevisionId" },
                values: new object[] { 1, 0, null, "", 0, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_SiteStylesheetRevisions_CreatedOn",
                table: "SiteStylesheetRevisions",
                column: "CreatedOn",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SiteStylesheetRevisions_SiteStylesheetId",
                table: "SiteStylesheetRevisions",
                column: "SiteStylesheetId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteStylesheets_PublishedRevisionId",
                table: "SiteStylesheets",
                column: "PublishedRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SiteStylesheetRevisions_SiteStylesheets_SiteStylesheetId",
                table: "SiteStylesheetRevisions",
                column: "SiteStylesheetId",
                principalTable: "SiteStylesheets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteStylesheetRevisions_SiteStylesheets_SiteStylesheetId",
                table: "SiteStylesheetRevisions");

            migrationBuilder.DropTable(
                name: "SiteStylesheets");

            migrationBuilder.DropTable(
                name: "SiteStylesheetRevisions");
        }
    }
}
