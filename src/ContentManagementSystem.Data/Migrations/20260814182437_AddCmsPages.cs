using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceVersionId = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    ZoneKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PropertyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    PinnedVersionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EditLocks",
                columns: table => new
                {
                    PageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AcquiredOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    HeartbeatOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditLocks", x => x.PageId);
                    table.ForeignKey(
                        name: "FK_EditLocks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UseExplicitUrl = table.Column<bool>(type: "bit", nullable: false),
                    ExplicitUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    DraftVersionId = table.Column<int>(type: "int", nullable: true),
                    PublishedVersionId = table.Column<int>(type: "int", nullable: true),
                    ShowInNavigation = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InternalNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_Pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pages_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Pages_Pages_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pages_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PageVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    TemplateRevision = table.Column<int>(type: "int", nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CanonicalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RobotsIndex = table.Column<bool>(type: "bit", nullable: false),
                    RobotsFollow = table.Column<bool>(type: "bit", nullable: false),
                    OgTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OgDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OgImageMediaId = table.Column<int>(type: "int", nullable: true),
                    OgType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TwitterCard = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StructuredDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeFreq = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Priority = table.Column<decimal>(type: "decimal(2,1)", nullable: true),
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
                    table.PrimaryKey("PK_PageVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageVersions_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PageVersions_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_HomePageId",
                table: "SiteSettings",
                column: "HomePageId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_NotFoundPageId",
                table: "SiteSettings",
                column: "NotFoundPageId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReferences_SourceType_SourceVersionId",
                table: "ContentReferences",
                columns: new[] { "SourceType", "SourceVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReferences_TargetType_TargetId",
                table: "ContentReferences",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_EditLocks_UserId",
                table: "EditLocks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_DraftVersionId",
                table: "Pages",
                column: "DraftVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_OwnerUserId",
                table: "Pages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ParentId_SortOrder_Live",
                table: "Pages",
                columns: new[] { "ParentId", "SortOrder" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Path",
                table: "Pages",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_PublicId",
                table: "Pages",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_PublishedVersionId",
                table: "Pages",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_TemplateId",
                table: "Pages",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_PageId_Status",
                table: "PageVersions",
                columns: new[] { "PageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_PageId_VersionNumber",
                table: "PageVersions",
                columns: new[] { "PageId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_Status_PublishOn",
                table: "PageVersions",
                columns: new[] { "Status", "PublishOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_TemplateId",
                table: "PageVersions",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_SiteSettings_Pages_HomePageId",
                table: "SiteSettings",
                column: "HomePageId",
                principalTable: "Pages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteSettings_Pages_NotFoundPageId",
                table: "SiteSettings",
                column: "NotFoundPageId",
                principalTable: "Pages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EditLocks_Pages_PageId",
                table: "EditLocks",
                column: "PageId",
                principalTable: "Pages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Pages_PageVersions_DraftVersionId",
                table: "Pages",
                column: "DraftVersionId",
                principalTable: "PageVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pages_PageVersions_PublishedVersionId",
                table: "Pages",
                column: "PublishedVersionId",
                principalTable: "PageVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteSettings_Pages_HomePageId",
                table: "SiteSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_SiteSettings_Pages_NotFoundPageId",
                table: "SiteSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_PageVersions_Pages_PageId",
                table: "PageVersions");

            migrationBuilder.DropTable(
                name: "ContentReferences");

            migrationBuilder.DropTable(
                name: "EditLocks");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "PageVersions");

            migrationBuilder.DropIndex(
                name: "IX_SiteSettings_HomePageId",
                table: "SiteSettings");

            migrationBuilder.DropIndex(
                name: "IX_SiteSettings_NotFoundPageId",
                table: "SiteSettings");
        }
    }
}
