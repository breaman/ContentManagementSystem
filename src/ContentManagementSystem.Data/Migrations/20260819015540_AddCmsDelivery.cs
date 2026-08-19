using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Migration #8 (task P8-14): managed navigation, tags, the search projection, and the outbox.
    /// <para>
    /// The full-text catalog and index are raw SQL rather than model, because EF Core models
    /// neither — and they are <strong>guarded on <c>SERVERPROPERTY('IsFullTextInstalled')</c></strong>,
    /// which is the Azure SQL versus on-premises difference this migration has to handle. SQL Server
    /// and Azure SQL Database both report 1 and get the index. Azure SQL Edge — the arm64 fallback
    /// the integration suite runs on, and a legitimate small deployment target — has no full-text
    /// engine at all, and a bare <c>CREATE FULLTEXT CATALOG</c> there fails the whole migration.
    /// The guard is what lets one migration apply everywhere; the search service asks the same
    /// question at runtime and falls back to a scan when the answer is no.
    /// </para>
    /// </remarks>
    public partial class AddCmsDelivery : Migration
    {
        /// <summary>Name of the full-text catalog the search index lives in.</summary>
        public const string SearchCatalogName = "CmsSearchCatalog";

        /// <summary>
        /// Creates the catalog and the index, on an instance that has a full-text engine.
        /// </summary>
        /// <remarks>
        /// Both statements are wrapped in <c>EXEC</c>: <c>CREATE FULLTEXT CATALOG</c> and
        /// <c>CREATE FULLTEXT INDEX</c> must each begin their own batch, which they cannot do
        /// inside an <c>IF</c>.
        /// </remarks>
        private const string CreateFullTextIndexSql = $"""
            IF SERVERPROPERTY('IsFullTextInstalled') = 1
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'{SearchCatalogName}')
                    EXEC(N'CREATE FULLTEXT CATALOG {SearchCatalogName}');

                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.SearchDocuments'))
                    EXEC(N'CREATE FULLTEXT INDEX ON dbo.SearchDocuments (Title, Body, Keywords)
                           KEY INDEX PK_SearchDocuments ON {SearchCatalogName} WITH CHANGE_TRACKING AUTO');
            END
            """;

        /// <summary>Drops them again, before the table itself goes.</summary>
        private const string DropFullTextIndexSql = $"""
            IF SERVERPROPERTY('IsFullTextInstalled') = 1
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.SearchDocuments'))
                    EXEC(N'DROP FULLTEXT INDEX ON dbo.SearchDocuments');

                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'{SearchCatalogName}')
                    EXEC(N'DROP FULLTEXT CATALOG {SearchCatalogName}');
            END
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NavigationMenus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationMenus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ProcessedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Keywords = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NavigationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NavigationMenuId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OpenInNewTab = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationItems", x => x.Id);
                    table.CheckConstraint("CK_NavigationItems_OneTarget", "(CASE WHEN [PageId] IS NULL THEN 0 ELSE 1 END +\n CASE WHEN [ExternalUrl] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_NavigationItems_NavigationItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "NavigationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NavigationItems_NavigationMenus_NavigationMenuId",
                        column: x => x.NavigationMenuId,
                        principalTable: "NavigationMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NavigationItems_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PageTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageTags_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PageTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NavigationItems_Menu_Parent_SortOrder",
                table: "NavigationItems",
                columns: new[] { "NavigationMenuId", "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_NavigationItems_PageId",
                table: "NavigationItems",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_NavigationItems_ParentId",
                table: "NavigationItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_NavigationMenus_Key",
                table: "NavigationMenus",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "CreatedOn",
                filter: "[ProcessedOn] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PageTags_PageId_TagId",
                table: "PageTags",
                columns: new[] { "PageId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageTags_TagId",
                table: "PageTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchDocuments_EntityType_EntityId",
                table: "SearchDocuments",
                columns: new[] { "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);
        
            migrationBuilder.Sql(CreateFullTextIndexSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // First: a table carrying a full-text index cannot be dropped while it has one.
            migrationBuilder.Sql(DropFullTextIndexSql);

            migrationBuilder.DropTable(
                name: "NavigationItems");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PageTags");

            migrationBuilder.DropTable(
                name: "SearchDocuments");

            migrationBuilder.DropTable(
                name: "NavigationMenus");

            migrationBuilder.DropTable(
                name: "Tags");
        }
    }
}
