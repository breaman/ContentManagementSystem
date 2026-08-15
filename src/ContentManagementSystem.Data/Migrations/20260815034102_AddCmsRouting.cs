using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotFoundLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UrlHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Referrer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HitCount = table.Column<long>(type: "bigint", nullable: false),
                    FirstSeenOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    LastSeenOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotFoundLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UrlHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageRoutes_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreviewTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    PageVersionId = table.Column<int>(type: "int", nullable: false),
                    ExpiresOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    RevokedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    RevokedBy = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreviewTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreviewTokens_PageVersions_PageVersionId",
                        column: x => x.PageVersionId,
                        principalTable: "PageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PreviewTokens_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Redirects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FromUrlHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ToUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ToPageId = table.Column<int>(type: "int", nullable: true),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HitCount = table.Column<long>(type: "bigint", nullable: false),
                    LastHitOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redirects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Redirects_Pages_ToPageId",
                        column: x => x.ToPageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotFoundLogs_HitCount",
                table: "NotFoundLogs",
                column: "HitCount",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_NotFoundLogs_UrlHash",
                table: "NotFoundLogs",
                column: "UrlHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageRoutes_PageId",
                table: "PageRoutes",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRoutes_UrlHash",
                table: "PageRoutes",
                column: "UrlHash");

            migrationBuilder.CreateIndex(
                name: "IX_PageRoutes_UrlHash_Published",
                table: "PageRoutes",
                column: "UrlHash",
                unique: true,
                filter: "[IsPublished] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PreviewTokens_PageId",
                table: "PreviewTokens",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PreviewTokens_PageVersionId",
                table: "PreviewTokens",
                column: "PageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PreviewTokens_TokenHash",
                table: "PreviewTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_FromUrlHash",
                table: "Redirects",
                column: "FromUrlHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_ToPageId",
                table: "Redirects",
                column: "ToPageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotFoundLogs");

            migrationBuilder.DropTable(
                name: "PageRoutes");

            migrationBuilder.DropTable(
                name: "PreviewTokens");

            migrationBuilder.DropTable(
                name: "Redirects");
        }
    }
}
