using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ComponentTypeName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IconKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SummaryTemplate = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsOrphaned = table.Column<bool>(type: "bit", nullable: false),
                    CurrentRevision = table.Column<int>(type: "int", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Compositions",
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
                    table.PrimaryKey("PK_Compositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "en-US"),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RobotsTxt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowMode = table.Column<int>(type: "int", nullable: false),
                    HomePageId = table.Column<int>(type: "int", nullable: true),
                    NotFoundPageId = table.Column<int>(type: "int", nullable: true),
                    VersionRetentionDays = table.Column<int>(type: "int", nullable: false),
                    DefaultOgImageMediaId = table.Column<int>(type: "int", nullable: true),
                    GoogleSiteVerification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                    table.CheckConstraint("CK_SiteSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ComponentTypeName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsOrphaned = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CurrentRevision = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockTypeProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockTypeId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FieldTypeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTypeProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockTypeProperties_BlockTypes_BlockTypeId",
                        column: x => x.BlockTypeId,
                        principalTable: "BlockTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlockTypeRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockTypeId = table.Column<int>(type: "int", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    PropertySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTypeRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockTypeRevisions_BlockTypes_BlockTypeId",
                        column: x => x.BlockTypeId,
                        principalTable: "BlockTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlockTypeCompositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockTypeId = table.Column<int>(type: "int", nullable: false),
                    CompositionId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTypeCompositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockTypeCompositions_BlockTypes_BlockTypeId",
                        column: x => x.BlockTypeId,
                        principalTable: "BlockTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlockTypeCompositions_Compositions_CompositionId",
                        column: x => x.CompositionId,
                        principalTable: "Compositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompositionProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompositionId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FieldTypeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompositionProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompositionProperties_Compositions_CompositionId",
                        column: x => x.CompositionId,
                        principalTable: "Compositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ZoneSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateRevisions_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FieldTypeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsInlineEditable = table.Column<bool>(type: "bit", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zones_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BlockTypes",
                columns: new[] { "Id", "ComponentTypeName", "CreatedBy", "CreatedOn", "CurrentRevision", "Description", "IconKey", "IsBuiltIn", "IsOrphaned", "Key", "ModifiedBy", "ModifiedOn", "Name", "SummaryTemplate" },
                values: new object[] { 1, null, 0, null, 1, "A single block of HTML, sanitized on save and again on render.", "code", true, false, "rawHtml", 0, null, "Raw HTML", "{content}" });

            migrationBuilder.InsertData(
                table: "SiteSettings",
                columns: new[] { "Id", "CreatedBy", "CreatedOn", "Culture", "DefaultOgImageMediaId", "GoogleSiteVerification", "HomePageId", "ModifiedBy", "ModifiedOn", "NotFoundPageId", "RobotsTxt", "SiteName", "TimeZoneId", "VersionRetentionDays", "WorkflowMode" },
                values: new object[] { 1, 0, null, "en-US", null, null, null, 0, null, null, null, "Content Management System", "UTC", 0, 0 });

            migrationBuilder.InsertData(
                table: "BlockTypeProperties",
                columns: new[] { "Id", "BlockTypeId", "ConfigurationJson", "CreatedBy", "CreatedOn", "Description", "FieldTypeKey", "Group", "IsRequired", "Key", "ModifiedBy", "ModifiedOn", "Name", "SortOrder" },
                values: new object[] { 1, 1, null, 0, null, null, "html", null, true, "content", 0, null, "HTML", 0 });

            migrationBuilder.InsertData(
                table: "BlockTypeRevisions",
                columns: new[] { "Id", "BlockTypeId", "CreatedBy", "CreatedOn", "ModifiedBy", "ModifiedOn", "Notes", "PropertySnapshotJson", "RevisionNumber" },
                values: new object[] { 1, 1, 0, null, 0, null, "Initial built-in definition.", "{\"properties\":[{\"key\":\"content\",\"name\":\"HTML\",\"fieldTypeKey\":\"html\",\"isRequired\":true,\"sortOrder\":0}]}", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_BlockTypeCompositions_BlockTypeId_CompositionId",
                table: "BlockTypeCompositions",
                columns: new[] { "BlockTypeId", "CompositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockTypeCompositions_CompositionId",
                table: "BlockTypeCompositions",
                column: "CompositionId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTypeProperties_BlockTypeId_Key",
                table: "BlockTypeProperties",
                columns: new[] { "BlockTypeId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockTypeRevisions_BlockTypeId_RevisionNumber",
                table: "BlockTypeRevisions",
                columns: new[] { "BlockTypeId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockTypes_Key",
                table: "BlockTypes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompositionProperties_CompositionId_Key",
                table: "CompositionProperties",
                columns: new[] { "CompositionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Compositions_Key",
                table: "Compositions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateRevisions_TemplateId_RevisionNumber",
                table: "TemplateRevisions",
                columns: new[] { "TemplateId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Key",
                table: "Templates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zones_TemplateId_Key",
                table: "Zones",
                columns: new[] { "TemplateId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockTypeCompositions");

            migrationBuilder.DropTable(
                name: "BlockTypeProperties");

            migrationBuilder.DropTable(
                name: "BlockTypeRevisions");

            migrationBuilder.DropTable(
                name: "CompositionProperties");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropTable(
                name: "TemplateRevisions");

            migrationBuilder.DropTable(
                name: "Zones");

            migrationBuilder.DropTable(
                name: "BlockTypes");

            migrationBuilder.DropTable(
                name: "Compositions");

            migrationBuilder.DropTable(
                name: "Templates");
        }
    }
}
