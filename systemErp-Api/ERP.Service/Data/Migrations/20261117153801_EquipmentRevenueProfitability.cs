using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentRevenueProfitability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WorkUnits",
                table: "SiteResourceUsage",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "EquipmentRevenueRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ShiftHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumDailyHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdleBillingPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyBillingPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentRevenueRate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentRevenueRate_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentRevenueRate_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentRevenueRate_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_ContractId",
                table: "EquipmentRevenueRate",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_ContractItemId",
                table: "EquipmentRevenueRate",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_EquipmentId",
                table: "EquipmentRevenueRate",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_TenantId",
                table: "EquipmentRevenueRate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_TenantId_ContractItemId",
                table: "EquipmentRevenueRate",
                columns: new[] { "TenantId", "ContractItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRevenueRate_TenantId_EquipmentId_ContractId_EffectiveFrom",
                table: "EquipmentRevenueRate",
                columns: new[] { "TenantId", "EquipmentId", "ContractId", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentRevenueRate");

            migrationBuilder.DropColumn(
                name: "WorkUnits",
                table: "SiteResourceUsage");
        }
    }
}
