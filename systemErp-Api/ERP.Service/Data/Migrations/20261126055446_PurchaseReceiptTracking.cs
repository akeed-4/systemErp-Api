using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseReceiptTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQty",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RejectedQty",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "DeliveryNoteItem",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseSupplyTracking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpectedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: "pending"),
                    LastReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseSupplyTracking", x => x.Id);
                    table.CheckConstraint("CK_PurchaseSupplyTrackings_SourceType", "[SourceType] IN ('purchase_invoice', 'contract')");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseSupplyEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseSupplyEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseSupplyEvent_PurchaseSupplyTracking_TrackingId",
                        column: x => x.TrackingId,
                        principalTable: "PurchaseSupplyTracking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryNoteItems_ReceivedQty",
                table: "DeliveryNoteItem",
                sql: "[ReceivedQty] IS NULL OR [ReceivedQty] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryNoteItems_RejectedQty",
                table: "DeliveryNoteItem",
                sql: "[RejectedQty] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyEvent_TenantId",
                table: "PurchaseSupplyEvent",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyEvent_TenantId_TrackingId_OccurredAt",
                table: "PurchaseSupplyEvent",
                columns: new[] { "TenantId", "TrackingId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyEvent_TrackingId",
                table: "PurchaseSupplyEvent",
                column: "TrackingId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyTracking_TenantId",
                table: "PurchaseSupplyTracking",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyTracking_TenantId_SourceDate",
                table: "PurchaseSupplyTracking",
                columns: new[] { "TenantId", "SourceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyTracking_TenantId_Status_ExpectedDate",
                table: "PurchaseSupplyTracking",
                columns: new[] { "TenantId", "Status", "ExpectedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseSupplyTracking_TenantId_SupplierId",
                table: "PurchaseSupplyTracking",
                columns: new[] { "TenantId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseSupplyTrackings_ActiveSource",
                table: "PurchaseSupplyTracking",
                columns: new[] { "TenantId", "SourceType", "SourceId" },
                unique: true,
                filter: "[Status] <> 'cancelled' AND [Status] <> 'rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseSupplyEvent");

            migrationBuilder.DropTable(
                name: "PurchaseSupplyTracking");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryNoteItems_ReceivedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryNoteItems_RejectedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "ReceivedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "RejectedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DeliveryNoteItem");
        }
    }
}
