using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// محاضر تسليم معدات التأجير واستلامها (R2b): المحضر وأسطره (العدّاد والوقود والحالة والانتقال من عقد سابق). جداول جديدة فقط؛
    /// حالة المعدة «rented» قيمة نصية جديدة في عمود قائم.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121100000_RentalHandovers")]
    public partial class RentalHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalHandover",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoverNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HandoverAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CounterpartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CounterpartyIdNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CounterpartyPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalHandover", x => x.Id);
                    table.ForeignKey(name: "FK_RentalHandover_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalHandover_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalHandoverLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Meter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MeterBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    FuelPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ConditionNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Accessories = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CarriedFromLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalHandoverLine", x => x.Id);
                    table.ForeignKey(name: "FK_RentalHandoverLine_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalHandoverLine_RentalContractLine_CarriedFromLineId", column: x => x.CarriedFromLineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalHandoverLine_RentalContractLine_ContractLineId", column: x => x.ContractLineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalHandoverLine_RentalHandover_HandoverId", column: x => x.HandoverId, principalTable: "RentalHandover",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalHandover_ContractId", table: "RentalHandover", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandover_TenantId", table: "RentalHandover", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandover_WarehouseId", table: "RentalHandover", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandover_TenantId_ContractId", table: "RentalHandover", columns: new[] { "TenantId", "ContractId" });
            migrationBuilder.CreateIndex(name: "IX_RentalHandover_TenantId_HandoverNumber", table: "RentalHandover", columns: new[] { "TenantId", "HandoverNumber" }, unique: true);

            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_CarriedFromLineId", table: "RentalHandoverLine", column: "CarriedFromLineId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_ContractLineId", table: "RentalHandoverLine", column: "ContractLineId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_EquipmentId", table: "RentalHandoverLine", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_HandoverId", table: "RentalHandoverLine", column: "HandoverId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_TenantId", table: "RentalHandoverLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalHandoverLine_TenantId_ContractLineId", table: "RentalHandoverLine", columns: new[] { "TenantId", "ContractLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RentalHandoverLine");
            migrationBuilder.DropTable(name: "RentalHandover");
        }
    }
}
