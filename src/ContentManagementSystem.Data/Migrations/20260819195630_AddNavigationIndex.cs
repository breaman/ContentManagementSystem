using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNavigationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pages_Navigation",
                table: "Pages",
                columns: new[] { "Depth", "SortOrder" },
                filter: "[IsDeleted] = 0 AND [ShowInNavigation] = 1")
                .Annotation("SqlServer:Include", new[] { "ParentId", "PublishedVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_Navigation",
                table: "Pages");
        }
    }
}
