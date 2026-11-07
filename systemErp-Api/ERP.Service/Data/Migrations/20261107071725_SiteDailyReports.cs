using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteDailyReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectDailyPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDailyPlan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDailyPlan_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectResource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LibraryResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RateBasis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FuelPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaintenancePerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExtraCostPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysPerMonth = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeMultiplier = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RentalStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RentalEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeHourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectResource", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectResource_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteDailyReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDailyReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteDailyReport_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteHourEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotStart = table.Column<int>(type: "int", nullable: false),
                    SlotEnd = table.Column<int>(type: "int", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DowntimeMinutes = table.Column<int>(type: "int", nullable: false),
                    DowntimeReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHourEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteHourEntry_SiteDailyReport_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SiteDailyReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteMaterialUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMaterialUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteMaterialUsage_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteMaterialUsage_SiteDailyReport_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SiteDailyReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteResourceUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Hours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdleHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdleReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    HourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeHourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteResourceUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteResourceUsage_ProjectResource_ProjectResourceId",
                        column: x => x.ProjectResourceId,
                        principalTable: "ProjectResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteResourceUsage_SiteDailyReport_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SiteDailyReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDailyPlan_ContractId",
                table: "ProjectDailyPlan",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDailyPlan_TenantId",
                table: "ProjectDailyPlan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDailyPlan_TenantId_ContractId_ContractItemId_EffectiveFrom",
                table: "ProjectDailyPlan",
                columns: new[] { "TenantId", "ContractId", "ContractItemId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_ContractId",
                table: "ProjectResource",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_TenantId",
                table: "ProjectResource",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_TenantId_ContractId",
                table: "ProjectResource",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_TenantId_EmployeeId",
                table: "ProjectResource",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteDailyReport_ContractId",
                table: "SiteDailyReport",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDailyReport_TenantId",
                table: "SiteDailyReport",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDailyReport_TenantId_ContractId_ReportDate",
                table: "SiteDailyReport",
                columns: new[] { "TenantId", "ContractId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteHourEntry_ReportId",
                table: "SiteHourEntry",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteHourEntry_TenantId",
                table: "SiteHourEntry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteHourEntry_TenantId_ContractItemId",
                table: "SiteHourEntry",
                columns: new[] { "TenantId", "ContractItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteMaterialUsage_ProductId",
                table: "SiteMaterialUsage",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteMaterialUsage_ReportId",
                table: "SiteMaterialUsage",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteMaterialUsage_TenantId",
                table: "SiteMaterialUsage",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteResourceUsage_ProjectResourceId",
                table: "SiteResourceUsage",
                column: "ProjectResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteResourceUsage_ReportId",
                table: "SiteResourceUsage",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteResourceUsage_TenantId",
                table: "SiteResourceUsage",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectDailyPlan");

            migrationBuilder.DropTable(
                name: "SiteHourEntry");

            migrationBuilder.DropTable(
                name: "SiteMaterialUsage");

            migrationBuilder.DropTable(
                name: "SiteResourceUsage");

            migrationBuilder.DropTable(
                name: "ProjectResource");

            migrationBuilder.DropTable(
                name: "SiteDailyReport");
        }
    }
}
