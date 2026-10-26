using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PermissionCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "Voucher",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Voucher",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovalLimit",
                table: "UserRolePermission",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "UserRolePermission",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "All");

            migrationBuilder.AddColumn<string>(
                name: "ScopeWarehouseIds",
                table: "UserRolePermission",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomRoleId",
                table: "User",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Actions",
                table: "ScreenPermissionItem",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "SalesReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "SalesReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "SalesInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "SalesInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "Quotation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Quotation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "PurchaseReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "PurchaseReturn",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "PurchaseInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "PurchaseInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "JournalEntry",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "JournalEntry",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdvanceActions",
                table: "ContractStageDefinition",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AllowedActions",
                table: "ContractStageDefinition",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "CarSalesContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "CarSalesContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "CarProcurementOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "CarProcurementOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermissionChangeLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionChangeLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BaseRole = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDefinition", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "active_execution",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,post,close" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "approved_signed",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "approve,sign", "approve,sign,reject" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "draft",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,delete" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "final_closed",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "", "" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "initial_inspection",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "close", "edit,post,reject,close" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "legal_review",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "review", "review,reject" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "milestone_billing",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,post,close" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_agreement",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "approve", "edit,delete,approve,post" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_delivery",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,post" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_delivery_return",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,post" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_milestone_invoice",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "", "post,close" });

            migrationBuilder.CreateIndex(
                name: "IX_PermissionChangeLog_TenantId",
                table: "PermissionChangeLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionChangeLog_TenantId_Timestamp",
                table: "PermissionChangeLog",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinition_TenantId",
                table: "RoleDefinition",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinition_TenantId_Code",
                table: "RoleDefinition",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermissionChangeLog");

            migrationBuilder.DropTable(
                name: "RoleDefinition");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "ApprovalLimit",
                table: "UserRolePermission");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "UserRolePermission");

            migrationBuilder.DropColumn(
                name: "ScopeWarehouseIds",
                table: "UserRolePermission");

            migrationBuilder.DropColumn(
                name: "CustomRoleId",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Actions",
                table: "ScreenPermissionItem");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "JournalEntry");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "JournalEntry");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "AdvanceActions",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "AllowedActions",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "CarSalesContract");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CarSalesContract");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "CarProcurementOrder");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CarProcurementOrder");
        }
    }
}
