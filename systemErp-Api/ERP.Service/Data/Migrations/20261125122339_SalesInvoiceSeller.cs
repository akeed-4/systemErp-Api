using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceSeller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SellerEmployeeId",
                table: "SalesReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "SalesReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerEmployeeId",
                table: "SalesInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "SalesInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerEmployeeId",
                table: "PurchaseReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "PurchaseReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerEmployeeId",
                table: "PurchaseInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "PurchaseInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SellerEmployeeId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturn_SellerEmployeeId",
                table: "SalesReturn",
                column: "SellerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_SellerEmployeeId",
                table: "SalesInvoice",
                column: "SellerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_SellerEmployeeId",
                table: "PurchaseReturn",
                column: "SellerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_SellerEmployeeId",
                table: "PurchaseInvoice",
                column: "SellerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_SellerEmployeeId",
                table: "Invoice",
                column: "SellerEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Employee_SellerEmployeeId",
                table: "Invoice",
                column: "SellerEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoice_Employee_SellerEmployeeId",
                table: "PurchaseInvoice",
                column: "SellerEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturn_Employee_SellerEmployeeId",
                table: "PurchaseReturn",
                column: "SellerEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoice_Employee_SellerEmployeeId",
                table: "SalesInvoice",
                column: "SellerEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturn_Employee_SellerEmployeeId",
                table: "SalesReturn",
                column: "SellerEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Employee_SellerEmployeeId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoice_Employee_SellerEmployeeId",
                table: "PurchaseInvoice");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturn_Employee_SellerEmployeeId",
                table: "PurchaseReturn");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoice_Employee_SellerEmployeeId",
                table: "SalesInvoice");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturn_Employee_SellerEmployeeId",
                table: "SalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturn_SellerEmployeeId",
                table: "SalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoice_SellerEmployeeId",
                table: "SalesInvoice");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturn_SellerEmployeeId",
                table: "PurchaseReturn");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoice_SellerEmployeeId",
                table: "PurchaseInvoice");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_SellerEmployeeId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "SellerEmployeeId",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "SellerEmployeeId",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "SellerEmployeeId",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "SellerEmployeeId",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "SellerEmployeeId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "Invoice");
        }
    }
}
