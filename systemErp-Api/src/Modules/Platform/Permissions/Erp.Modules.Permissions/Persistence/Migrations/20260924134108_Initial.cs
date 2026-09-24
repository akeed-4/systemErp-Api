using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Permissions.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authz");

            migrationBuilder.CreateTable(
                name: "Screens",
                schema: "authz",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleScreenPermissions",
                schema: "authz",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreenId = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false),
                    CanEdit = table.Column<bool>(type: "bit", nullable: false),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false),
                    CanApprove = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleScreenPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleScreenPermissions_Screens_ScreenId",
                        column: x => x.ScreenId,
                        principalSchema: "authz",
                        principalTable: "Screens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserScreenPermissions",
                schema: "authz",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreenId = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false),
                    CanEdit = table.Column<bool>(type: "bit", nullable: false),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false),
                    CanApprove = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserScreenPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserScreenPermissions_Screens_ScreenId",
                        column: x => x.ScreenId,
                        principalSchema: "authz",
                        principalTable: "Screens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleScreenPermissions_TenantId_RoleCode_ScreenId",
                schema: "authz",
                table: "RoleScreenPermissions",
                columns: new[] { "TenantId", "RoleCode", "ScreenId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleScreenPermissions_TenantId_ScreenId",
                schema: "authz",
                table: "RoleScreenPermissions",
                columns: new[] { "TenantId", "ScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserScreenPermissions_TenantId_ScreenId",
                schema: "authz",
                table: "UserScreenPermissions",
                columns: new[] { "TenantId", "ScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserScreenPermissions_TenantId_UserId_ScreenId",
                schema: "authz",
                table: "UserScreenPermissions",
                columns: new[] { "TenantId", "UserId", "ScreenId" },
                unique: true);

            // Cross-module references (hand-written; identity is migrated before authz, §17.2).
            // They are not in the EF model, so later model diffs never touch them.
            migrationBuilder.AddForeignKey(
                name: "FK_RoleScreenPermissions_identity_Roles_RoleCode",
                schema: "authz",
                table: "RoleScreenPermissions",
                column: "RoleCode",
                principalSchema: "identity",
                principalTable: "Roles",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserScreenPermissions_identity_Users_UserId",
                schema: "authz",
                table: "UserScreenPermissions",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleScreenPermissions",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "UserScreenPermissions",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "Screens",
                schema: "authz");
        }
    }
}
