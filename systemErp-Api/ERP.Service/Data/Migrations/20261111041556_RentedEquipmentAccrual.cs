using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class RentedEquipmentAccrual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "IdleBillingPercent",
                table: "SiteResourceUsage",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandbyBillingPercent",
                table: "SiteResourceUsage",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandbyHours",
                table: "SiteResourceUsage",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CostRevision",
                table: "SiteDailyReport",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReopenNote",
                table: "SiteDailyReport",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IdleBillingPercent",
                table: "ProjectResource",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandbyBillingPercent",
                table: "ProjectResource",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ProjectAccruedRentalAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectRentedEquipmentCostAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SiteRentalAccrual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ClosedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteRentalAccrual", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrual_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrual_CostCenter_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrual_ProjectResource_ProjectResourceId",
                        column: x => x.ProjectResourceId,
                        principalTable: "ProjectResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrual_SiteDailyReport_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SiteDailyReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrual_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteRentalAccrualMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccrualId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteRentalAccrualMovement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteRentalAccrualMovement_SiteRentalAccrual_AccrualId",
                        column: x => x.AccrualId,
                        principalTable: "SiteRentalAccrual",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_ContractId",
                table: "SiteRentalAccrual",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_CostCenterId",
                table: "SiteRentalAccrual",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_ProjectResourceId",
                table: "SiteRentalAccrual",
                column: "ProjectResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_ReportId",
                table: "SiteRentalAccrual",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_SupplierId",
                table: "SiteRentalAccrual",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_TenantId",
                table: "SiteRentalAccrual",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_TenantId_ReportId_ProjectResourceId",
                table: "SiteRentalAccrual",
                columns: new[] { "TenantId", "ReportId", "ProjectResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrual_TenantId_SupplierId_CostCenterId",
                table: "SiteRentalAccrual",
                columns: new[] { "TenantId", "SupplierId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrualMovement_AccrualId_Revision",
                table: "SiteRentalAccrualMovement",
                columns: new[] { "AccrualId", "Revision" },
                unique: true,
                filter: "[Kind] IN (N'accrual', N'adjustment')");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrualMovement_TenantId",
                table: "SiteRentalAccrualMovement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteRentalAccrualMovement_TenantId_SupplierId_CostCenterId",
                table: "SiteRentalAccrualMovement",
                columns: new[] { "TenantId", "SupplierId", "CostCenterId" });

            // التوقف كان يُحتسب كاملاً لكل الموارد (أعلاه: النسبة الافتراضية 100). التقارير المعتمدة التي رُحِّل قيد تحميلها
            // تُعد مرحّلة مرة أولى، فلا يعيد «ترحيل التكاليف المعلّقة» ترحيلها، وإعادة فتحها ترحّل الفرق عمّا رُحِّل فقط.
            migrationBuilder.Sql("UPDATE [SiteDailyReport] SET [CostRevision] = 1 WHERE [Status] = N'approved' AND [CostJournalEntryId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteRentalAccrualMovement");

            migrationBuilder.DropTable(
                name: "SiteRentalAccrual");

            migrationBuilder.DropColumn(
                name: "IdleBillingPercent",
                table: "SiteResourceUsage");

            migrationBuilder.DropColumn(
                name: "StandbyBillingPercent",
                table: "SiteResourceUsage");

            migrationBuilder.DropColumn(
                name: "StandbyHours",
                table: "SiteResourceUsage");

            migrationBuilder.DropColumn(
                name: "CostRevision",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "ReopenNote",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "IdleBillingPercent",
                table: "ProjectResource");

            migrationBuilder.DropColumn(
                name: "StandbyBillingPercent",
                table: "ProjectResource");

            migrationBuilder.DropColumn(
                name: "ProjectAccruedRentalAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ProjectRentedEquipmentCostAccountCode",
                table: "AccountingSettings");
        }
    }
}
