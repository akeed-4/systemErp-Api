using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ظ…ط­ط±ظƒ ظ‚ظˆط§ظ„ط¨ ط§ظ„ط¯ظ„ظٹظ„ ط§ظ„ظ…ط­ط§ط³ط¨ظٹ: ط§ظ„ط£ظ†ط´ط·ط© ظˆط§ظ„ظ‚ظˆط§ظ„ط¨ ظˆط­ط³ط§ط¨ط§طھظ‡ط§ (ط¹ظ„ظ‰ ظ…ط³طھظˆظ‰ ط§ظ„ظ…ظ†طµط©طŒ طھظڈط²ط±ط¹ ط¹ظ†ط¯ ط£ظˆظ„ ظ‚ط±ط§ط،ط© ظ…ظ† ChartTemplateCatalog ظ„ط§ ظ‡ظ†ط§)طŒ
    /// ظˆط­ط§ظ„ط© طھط£ط³ظٹط³ ط¯ظ„ظٹظ„ ظƒظ„ ظ…ظ†ط´ط£ط© ظˆط±ط¨ط· ط§ظ„ظ…ظپط§طھظٹط­ ط§ظ„ظˆط¸ظٹظپظٹط© ط§ظ„ط¹ط§ظ…ط©طŒ ظˆظ†ط´ط§ط· ط§ظ„ظ…ظ†ط´ط£ط©. ط§ظ„ظ…ظ†ط´ط¢طھ ط§ظ„ظ‚ط§ط¦ظ…ط© ط¨ظ„ط§ ظ†ط´ط§ط· ظˆظ„ط§ ط³ط¬ظ„ طھط£ط³ظٹط³: ظ„ط§ ظٹظڈظ…ط³ ط¯ظ„ظٹظ„ظ‡ط§.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261127150000_ChartOfAccountsTemplates")]
    public partial class ChartOfAccountsTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityKey",
                table: "Tenant",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessActivity",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ParentKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessActivity", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ChartTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ActivityKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModuleKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChartTemplateAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsGroup = table.Column<bool>(type: "bit", nullable: false),
                    SemanticKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartTemplateAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChartTemplateAccount_ChartTemplate_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ChartTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChartProvisioning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AppliedTemplates = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AppliedModules = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AccountsCreated = table.Column<int>(type: "int", nullable: false),
                    BindingsCreated = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartProvisioning", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemanticKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBinding", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChartTemplate_Key",
                table: "ChartTemplate",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartTemplate_Kind_ActivityKey_ModuleKey",
                table: "ChartTemplate",
                columns: new[] { "Kind", "ActivityKey", "ModuleKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ChartTemplateAccount_TemplateId_Code",
                table: "ChartTemplateAccount",
                columns: new[] { "TemplateId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChartTemplateAccount_TemplateId_SemanticKey",
                table: "ChartTemplateAccount",
                columns: new[] { "TemplateId", "SemanticKey" },
                unique: true,
                filter: "[SemanticKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChartProvisioning_TenantId",
                table: "ChartProvisioning",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountBinding_TenantId",
                table: "AccountBinding",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBinding_TenantId_SemanticKey",
                table: "AccountBinding",
                columns: new[] { "TenantId", "SemanticKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AccountBinding");
            migrationBuilder.DropTable(name: "ChartProvisioning");
            migrationBuilder.DropTable(name: "ChartTemplateAccount");
            migrationBuilder.DropTable(name: "ChartTemplate");
            migrationBuilder.DropTable(name: "BusinessActivity");
            migrationBuilder.DropColumn(name: "ActivityKey", table: "Tenant");
        }
    }
}
