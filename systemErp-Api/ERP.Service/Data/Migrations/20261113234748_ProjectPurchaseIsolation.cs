using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectPurchaseIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryNoteId",
                table: "ProjectMaterialIssue",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoqItemId",
                table: "MaterialRequisitionItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodyIssueId",
                table: "DeliveryReturnNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoqItemId",
                table: "DeliveryNoteItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodyIssueId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectGoodsReceivedNotInvoicedAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssue_TenantId_DeliveryNoteId",
                table: "ProjectMaterialIssue",
                columns: new[] { "TenantId", "DeliveryNoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_RequisitionId",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "RequisitionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectMaterialIssue_TenantId_DeliveryNoteId",
                table: "ProjectMaterialIssue");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_RequisitionId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryNoteId",
                table: "ProjectMaterialIssue");

            migrationBuilder.DropColumn(
                name: "BoqItemId",
                table: "MaterialRequisitionItem");

            migrationBuilder.DropColumn(
                name: "CustodyIssueId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "BoqItemId",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "CustodyIssueId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "ProjectGoodsReceivedNotInvoicedAccountCode",
                table: "AccountingSettings");
        }
    }
}
