using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// مهام نقل معدات التأجير (R2c): المهمة وأسطرها (المعدات المنقولة). جداول جديدة فقط؛ تغيير المشغّل يُسجَّل في سجل تغييرات العقد
    /// القائم بنوع نصي جديد «operator».
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121110000_RentalTransport")]
    public partial class RentalTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalTransportJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FromLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ToLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DriverEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransportEquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VehicleDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ChargeAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalTransportJob", x => x.Id);
                    table.ForeignKey(name: "FK_RentalTransportJob_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJob_Employee_DriverEmployeeId", column: x => x.DriverEmployeeId, principalTable: "Employee",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJob_RentalHandover_HandoverId", column: x => x.HandoverId, principalTable: "RentalHandover",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJob_Supplier_SupplierId", column: x => x.SupplierId, principalTable: "Supplier",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJob_Equipment_TransportEquipmentId", column: x => x.TransportEquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalTransportJobLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalTransportJobLine", x => x.Id);
                    table.ForeignKey(name: "FK_RentalTransportJobLine_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJobLine_RentalContractLine_ContractLineId", column: x => x.ContractLineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTransportJobLine_RentalTransportJob_JobId", column: x => x.JobId, principalTable: "RentalTransportJob",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_ContractId", table: "RentalTransportJob", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_DriverEmployeeId", table: "RentalTransportJob", column: "DriverEmployeeId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_HandoverId", table: "RentalTransportJob", column: "HandoverId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_SupplierId", table: "RentalTransportJob", column: "SupplierId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_TenantId", table: "RentalTransportJob", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_TransportEquipmentId", table: "RentalTransportJob", column: "TransportEquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_TenantId_ContractId", table: "RentalTransportJob", columns: new[] { "TenantId", "ContractId" });
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJob_TenantId_JobNumber", table: "RentalTransportJob", columns: new[] { "TenantId", "JobNumber" }, unique: true);

            migrationBuilder.CreateIndex(name: "IX_RentalTransportJobLine_ContractLineId", table: "RentalTransportJobLine", column: "ContractLineId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJobLine_EquipmentId", table: "RentalTransportJobLine", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJobLine_JobId", table: "RentalTransportJobLine", column: "JobId");
            migrationBuilder.CreateIndex(name: "IX_RentalTransportJobLine_TenantId", table: "RentalTransportJobLine", column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RentalTransportJobLine");
            migrationBuilder.DropTable(name: "RentalTransportJob");
        }
    }
}
