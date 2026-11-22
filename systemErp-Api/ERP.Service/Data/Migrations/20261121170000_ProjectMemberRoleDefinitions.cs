using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// أدوار أعضاء المشاريع التي تضيفها المنشأة (فني خطوط، سائق رافعة، مسؤول السلامة...) إضافة إلى أدوار النظام الثابتة في الكود.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121170000_ProjectMemberRoleDefinitions")]
    public partial class ProjectMemberRoleDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectMemberRoleDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ProjectMemberRoleDefinition", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_ProjectMemberRoleDefinition_TenantId", table: "ProjectMemberRoleDefinition", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_ProjectMemberRoleDefinition_TenantId_Code", table: "ProjectMemberRoleDefinition",
                columns: new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_ProjectMemberRoleDefinition_TenantId_NameAr", table: "ProjectMemberRoleDefinition",
                columns: new[] { "TenantId", "NameAr" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProjectMemberRoleDefinition");
        }
    }
}
