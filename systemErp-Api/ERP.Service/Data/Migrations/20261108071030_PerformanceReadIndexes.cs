using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_TenantId_ItemId_Date_CreatedAt",
                table: "StockMovement",
                columns: new[] { "TenantId", "ItemId", "Date", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_TenantId_SourceId",
                table: "StockMovement",
                columns: new[] { "TenantId", "SourceId" },
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturn_TenantId_OriginalInvoiceId",
                table: "SalesReturn",
                columns: new[] { "TenantId", "OriginalInvoiceId" },
                filter: "[OriginalInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_TenantId_IssueDate_InvoiceNumber_Id",
                table: "SalesInvoice",
                columns: new[] { "TenantId", "IssueDate", "InvoiceNumber", "Id" },
                descending: new[] { false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_TenantId_OriginalInvoiceId",
                table: "SalesInvoice",
                columns: new[] { "TenantId", "OriginalInvoiceId" },
                filter: "[OriginalInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_TenantId_PartyId_IssueDate",
                table: "SalesInvoice",
                columns: new[] { "TenantId", "PartyId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_TenantId_OriginalInvoiceId",
                table: "PurchaseReturn",
                columns: new[] { "TenantId", "OriginalInvoiceId" },
                filter: "[OriginalInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_TenantId_IssueDate_InvoiceNumber_Id",
                table: "PurchaseInvoice",
                columns: new[] { "TenantId", "IssueDate", "InvoiceNumber", "Id" },
                descending: new[] { false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_TenantId_OriginalInvoiceId",
                table: "PurchaseInvoice",
                columns: new[] { "TenantId", "OriginalInvoiceId" },
                filter: "[OriginalInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_TenantId_PartyId_IssueDate",
                table: "PurchaseInvoice",
                columns: new[] { "TenantId", "PartyId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_TenantId_CreatedAt_Id",
                table: "Product",
                columns: new[] { "TenantId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_TenantId_OriginalInvoiceId",
                table: "Invoice",
                columns: new[] { "TenantId", "OriginalInvoiceId" },
                filter: "[OriginalInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_TenantId_CreatedAt_Id",
                table: "Customer",
                columns: new[] { "TenantId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "DocumentId" })
                .Annotation("SqlServer:Include", new[] { "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovement_TenantId_ItemId_Date_CreatedAt",
                table: "StockMovement");

            migrationBuilder.DropIndex(
                name: "IX_StockMovement_TenantId_SourceId",
                table: "StockMovement");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturn_TenantId_OriginalInvoiceId",
                table: "SalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoice_TenantId_IssueDate_InvoiceNumber_Id",
                table: "SalesInvoice");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoice_TenantId_OriginalInvoiceId",
                table: "SalesInvoice");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoice_TenantId_PartyId_IssueDate",
                table: "SalesInvoice");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturn_TenantId_OriginalInvoiceId",
                table: "PurchaseReturn");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoice_TenantId_IssueDate_InvoiceNumber_Id",
                table: "PurchaseInvoice");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoice_TenantId_OriginalInvoiceId",
                table: "PurchaseInvoice");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoice_TenantId_PartyId_IssueDate",
                table: "PurchaseInvoice");

            migrationBuilder.DropIndex(
                name: "IX_Product_TenantId_CreatedAt_Id",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_TenantId_OriginalInvoiceId",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_Customer_TenantId_CreatedAt_Id",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest");
        }
    }
}
