using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryNoteSourcesAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedQty",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoicedQty",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLineId",
                table: "DeliveryNoteItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "DeliveryNote",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "DeliveryNote",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "DeliveryNote",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryConfirmedByUserId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "DeliveryNote",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "untracked");

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAt",
                table: "DeliveryNote",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverName",
                table: "DeliveryNote",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceNumber",
                table: "DeliveryNote",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "DeliveryNote",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.CreateTable(
                name: "DeliveryNoteEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryNoteEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteEvent_DeliveryNote_DeliveryNoteId",
                        column: x => x.DeliveryNoteId,
                        principalTable: "DeliveryNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryNoteInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryNoteItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNoteInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteInvoiceLine_DeliveryNoteItem_DeliveryNoteItemId",
                        column: x => x.DeliveryNoteItemId,
                        principalTable: "DeliveryNoteItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteInvoiceLine_DeliveryNote_DeliveryNoteId",
                        column: x => x.DeliveryNoteId,
                        principalTable: "DeliveryNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteItem_TenantId_SourceLineId",
                table: "DeliveryNoteItem",
                columns: new[] { "TenantId", "SourceLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryStatus_Date",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "DeliveryStatus", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_SourceType_SourceId",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteEvent_DeliveryNoteId",
                table: "DeliveryNoteEvent",
                column: "DeliveryNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteEvent_TenantId",
                table: "DeliveryNoteEvent",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteEvent_TenantId_DeliveryNoteId_OccurredAt",
                table: "DeliveryNoteEvent",
                columns: new[] { "TenantId", "DeliveryNoteId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_DeliveryNoteId",
                table: "DeliveryNoteInvoiceLine",
                column: "DeliveryNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_DeliveryNoteItemId",
                table: "DeliveryNoteInvoiceLine",
                column: "DeliveryNoteItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_TenantId",
                table: "DeliveryNoteInvoiceLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_TenantId_DeliveryNoteId",
                table: "DeliveryNoteInvoiceLine",
                columns: new[] { "TenantId", "DeliveryNoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_TenantId_DeliveryNoteItemId_InvoiceId",
                table: "DeliveryNoteInvoiceLine",
                columns: new[] { "TenantId", "DeliveryNoteItemId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteInvoiceLine_TenantId_InvoiceId",
                table: "DeliveryNoteInvoiceLine",
                columns: new[] { "TenantId", "InvoiceId" });

            // تعبئة متوافقة مع المعنى الفعلي للبيانات القائمة:
            // - المصدر حقيقة مسجلة: بيان مبيعات على عقد (لا سند استلام مشروع ولا أمر شراء) مصدره الاتفاقية، ويُربط سطره ببند الاتفاقية بالصنف.
            // - حالة التوصيل تبقى untracked (الافتراضي) ولا تُعبّأ الكمية المؤكدة: الحالة القديمة Delivered تعني «صدر البيان» لا «استلمه العميل»،
            //   فتبقى قواعد الفوترة والمرتجع والحذف القديمة لهذه البيانات كما هي، ولا تتغير الفواتير السابقة.
            migrationBuilder.Sql(@"
UPDATE n SET n.SourceType = N'contract', n.SourceId = n.ContractId, n.SourceNumber = n.ContractNumber
FROM DeliveryNote n
WHERE n.Type = N'sales_delivery' AND n.ContractId IS NOT NULL AND n.RequisitionId IS NULL AND n.PurchaseOrderId IS NULL;

UPDATE i SET i.SourceLineId = (
    SELECT TOP 1 ci.Id FROM CommercialContractItem ci
    WHERE ci.ContractId = n.ContractId AND ci.TenantId = n.TenantId AND ci.IsGroup = 0 AND (ci.ItemId = i.ItemId OR ci.Id = i.ItemId)
    ORDER BY ci.SortOrder)
FROM DeliveryNoteItem i
JOIN DeliveryNote n ON n.Id = i.DeliveryNoteId
WHERE n.SourceType = N'contract';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryNoteEvent");

            migrationBuilder.DropTable(
                name: "DeliveryNoteInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNoteItem_TenantId_SourceLineId",
                table: "DeliveryNoteItem");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryStatus_Date",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_SourceType_SourceId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "ConfirmedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "InvoicedQty",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "SourceLineId",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryConfirmedByUserId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "ReceiverName",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "SourceNumber",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "DeliveryNote");
        }
    }
}
