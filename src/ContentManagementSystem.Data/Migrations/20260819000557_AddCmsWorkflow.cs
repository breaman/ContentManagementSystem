using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ContentManagementSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RedirectToParentOnUnpublish",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    PageVersionId = table.Column<int>(type: "int", nullable: true),
                    ZoneKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ResolvedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_PageVersions_PageVersionId",
                        column: x => x.PageVersionId,
                        principalTable: "PageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Comments_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReadOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PageAcls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    PrincipalType = table.Column<int>(type: "int", nullable: false),
                    PrincipalId = table.Column<int>(type: "int", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsAllow = table.Column<bool>(type: "bit", nullable: false),
                    IsInherited = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageAcls_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    PageVersionId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RunOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ClaimedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ClaimedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_PageVersions_PageVersionId",
                        column: x => x.PageVersionId,
                        principalTable: "PageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    PageVersionId = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmissionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTasks_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowTasks_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowTasks_PageVersions_PageVersionId",
                        column: x => x.PageVersionId,
                        principalTable: "PageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowTasks_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 1, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0001", "Administrator", "ADMINISTRATOR" },
                    { 2, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0002", "Developer", "DEVELOPER" },
                    { 3, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0003", "Editor", "EDITOR" },
                    { 4, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0004", "Author", "AUTHOR" },
                    { 5, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0005", "Approver", "APPROVER" },
                    { 6, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0006", "MediaManager", "MEDIAMANAGER" },
                    { 7, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0007", "Viewer", "VIEWER" }
                });

            migrationBuilder.UpdateData(
                table: "SiteSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "RedirectToParentOnUnpublish",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PageId_Id",
                table: "Comments",
                columns: new[] { "PageId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PageVersionId",
                table: "Comments",
                column: "PageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ResolvedByUserId",
                table: "Comments",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PageId",
                table: "Notifications",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadOn_Id",
                table: "Notifications",
                columns: new[] { "UserId", "ReadOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_PageAcls_PageId_Principal_Permission",
                table: "PageAcls",
                columns: new[] { "PageId", "PrincipalType", "PrincipalId", "Permission" },
                unique: true)
                .Annotation("SqlServer:Include", new[] { "IsAllow", "IsInherited" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_OwnerUserId",
                table: "ScheduledJobs",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_PageVersionId",
                table: "ScheduledJobs",
                column: "PageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_State_RunOn",
                table: "ScheduledJobs",
                columns: new[] { "State", "RunOn" });

            migrationBuilder.CreateIndex(
                name: "UX_ScheduledJobs_PageId_Kind_Outstanding",
                table: "ScheduledJobs",
                columns: new[] { "PageId", "Kind" },
                unique: true,
                filter: "[State] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_AssignedToUserId_State",
                table: "WorkflowTasks",
                columns: new[] { "AssignedToUserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_DecidedByUserId",
                table: "WorkflowTasks",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_PageId_State",
                table: "WorkflowTasks",
                columns: new[] { "PageId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowTasks_PageVersionId_Pending",
                table: "WorkflowTasks",
                column: "PageVersionId",
                unique: true,
                filter: "[State] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PageAcls");

            migrationBuilder.DropTable(
                name: "ScheduledJobs");

            migrationBuilder.DropTable(
                name: "WorkflowTasks");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DropColumn(
                name: "RedirectToParentOnUnpublish",
                table: "SiteSettings");
        }
    }
}
