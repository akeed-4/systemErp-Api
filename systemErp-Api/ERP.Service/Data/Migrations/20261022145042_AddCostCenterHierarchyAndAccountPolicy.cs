using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCostCenterHierarchyAndAccountPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "Voucher",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "SalesReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "SalesInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "PurchaseReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "PurchaseInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "CostCenter",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterPolicy",
                table: "Account",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Optional");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLine_TenantId_CostCenterId",
                table: "JournalEntryLine",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenter_ParentId",
                table: "CostCenter",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenter_CostCenter_ParentId",
                table: "CostCenter",
                column: "ParentId",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostCenter_CostCenter_ParentId",
                table: "CostCenter");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLine_TenantId_CostCenterId",
                table: "JournalEntryLine");

            migrationBuilder.DropIndex(
                name: "IX_CostCenter_ParentId",
                table: "CostCenter");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "CostCenter");

            migrationBuilder.DropColumn(
                name: "CostCenterPolicy",
                table: "Account");
        }
    }
}
