using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceItemRentalClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceItemId",
                table: "SalesReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectResourceId",
                table: "SalesReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceItemId",
                table: "SalesInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectResourceId",
                table: "SalesInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceItemId",
                table: "PurchaseReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectResourceId",
                table: "PurchaseReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceItemId",
                table: "PurchaseInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectResourceId",
                table: "PurchaseInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceItemId",
                table: "InvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectResourceId",
                table: "InvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItem_OriginalInvoiceItemId",
                table: "SalesReturnItem",
                column: "OriginalInvoiceItemId",
                filter: "[OriginalInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItem_ProjectResourceId",
                table: "SalesReturnItem",
                column: "ProjectResourceId",
                filter: "[ProjectResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_OriginalInvoiceItemId",
                table: "SalesInvoiceItem",
                column: "OriginalInvoiceItemId",
                filter: "[OriginalInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_ProjectResourceId",
                table: "SalesInvoiceItem",
                column: "ProjectResourceId",
                filter: "[ProjectResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_OriginalInvoiceItemId",
                table: "PurchaseReturnItem",
                column: "OriginalInvoiceItemId",
                filter: "[OriginalInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_ProjectResourceId",
                table: "PurchaseReturnItem",
                column: "ProjectResourceId",
                filter: "[ProjectResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItem_OriginalInvoiceItemId",
                table: "PurchaseInvoiceItem",
                column: "OriginalInvoiceItemId",
                filter: "[OriginalInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItem_ProjectResourceId",
                table: "PurchaseInvoiceItem",
                column: "ProjectResourceId",
                filter: "[ProjectResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItem_OriginalInvoiceItemId",
                table: "InvoiceItem",
                column: "OriginalInvoiceItemId",
                filter: "[OriginalInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItem_ProjectResourceId",
                table: "InvoiceItem",
                column: "ProjectResourceId",
                filter: "[ProjectResourceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesReturnItem_OriginalInvoiceItemId",
                table: "SalesReturnItem");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnItem_ProjectResourceId",
                table: "SalesReturnItem");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItem_OriginalInvoiceItemId",
                table: "SalesInvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItem_ProjectResourceId",
                table: "SalesInvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturnItem_OriginalInvoiceItemId",
                table: "PurchaseReturnItem");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturnItem_ProjectResourceId",
                table: "PurchaseReturnItem");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItem_OriginalInvoiceItemId",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItem_ProjectResourceId",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItem_OriginalInvoiceItemId",
                table: "InvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItem_ProjectResourceId",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceItemId",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ProjectResourceId",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceItemId",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProjectResourceId",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceItemId",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ProjectResourceId",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceItemId",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProjectResourceId",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceItemId",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProjectResourceId",
                table: "InvoiceItem");
        }
    }
}
