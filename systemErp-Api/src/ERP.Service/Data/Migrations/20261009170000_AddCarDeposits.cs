using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>عربون السيارات: حقول سند العربون (قبض على مركبة) وربط العقد به. أعمدة اختيارية فتبقى السجلات القديمة سندات/عقوداً عادية.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261009170000_AddCarDeposits")]
    public partial class AddCarDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "DepositContractId", table: "Voucher", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "DepositCustomerId", table: "Voucher", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "DepositStatus", table: "Voucher", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "DepositVehicleId", table: "Voucher", type: "uniqueidentifier", nullable: true);

            migrationBuilder.AddColumn<decimal>(name: "DepositAppliedAmount", table: "CarSalesContract", type: "decimal(18,4)", precision: 18, scale: 4, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "DepositVoucherId", table: "CarSalesContract", type: "uniqueidentifier", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DepositContractId", table: "Voucher");
            migrationBuilder.DropColumn(name: "DepositCustomerId", table: "Voucher");
            migrationBuilder.DropColumn(name: "DepositStatus", table: "Voucher");
            migrationBuilder.DropColumn(name: "DepositVehicleId", table: "Voucher");
            migrationBuilder.DropColumn(name: "DepositAppliedAmount", table: "CarSalesContract");
            migrationBuilder.DropColumn(name: "DepositVoucherId", table: "CarSalesContract");
        }
    }
}
