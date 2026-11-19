using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceForeignKeys : Migration
    {
        // جهة المستند حين لا يحدّد PartyType الطرف صراحةً (يطابق IsSupplierParty في الكيانات).
        private static readonly (string Table, string PurchaseSide)[] PartyDocuments =
        {
            ("Invoice", "d.[Kind] IN ('Purchase','PurchaseReturn')"),
            ("Quotation", "d.[Type] LIKE 'purchase%'"),
            ("CommercialOrder", "d.[Type] LIKE 'purchase%'"),
            ("Agreement", "d.[Type] = 'purchase'"),
            ("CommercialContract", "d.[ContractType] LIKE '%purchas%'"),
            ("DeliveryNote", "d.[Type] LIKE 'purchase%'"),
        };

        // أعمدة اختيارية قديمة: أي مرجع معلّق (يشير لسجل محذوف) يُفرَّغ قبل إنشاء القيد حتى لا تفشل الترقية.
        private static readonly (string Table, string Column, string Principal)[] NullableReferences =
        {
            ("AppNotification", "ApprovalRequestId", "ApprovalRequest"),
            ("ApprovalPolicyStep", "ApproverUserId", "User"),
            ("ApprovalRequest", "RequiredApproverUserId", "User"),
            ("CarAgent", "BrandId", "CarBrand"),
            ("CarModel", "AgentId", "CarAgent"),
            ("CarProcurementOrder", "PurchaseInvoiceId", "Invoice"),
            ("CarProcurementOrderVin", "ItemId", "CarProcurementOrderItem"),
            ("CarProcurementOrderVin", "VehicleId", "Vehicle"),
            ("CarSalesContract", "CustomerId", "Customer"),
            ("CarSalesContract", "FinancingBankId", "BankEntity"),
            ("CarSalesContract", "InvoiceId", "Invoice"),
            ("CommercialContract", "CostCenterId", "CostCenter"),
            ("CommercialContractItem", "ItemId", "Product"),
            ("CommercialOrder", "AgreementId", "Agreement"),
            ("CommercialOrder", "ConvertedInvoiceId", "Invoice"),
            ("ContractMilestone", "InvoiceId", "Invoice"),
            ("DeliveryNote", "ContractId", "CommercialContract"),
            ("DeliveryNote", "InvoiceId", "Invoice"),
            ("DeliveryReturnNote", "ContractId", "CommercialContract"),
            ("Invoice", "JournalEntryId", "JournalEntry"),
            ("Invoice", "OriginalInvoiceId", "Invoice"),
            ("InvoiceItem", "CostCenterId", "CostCenter"),
            ("InvoiceVehicleLine", "BrandId", "CarBrand"),
            ("InvoiceVehicleLine", "ModelId", "CarModel"),
            ("InvoiceVehicleLine", "TrimId", "CarTrim"),
            ("InvoiceVehicleLine", "VehicleId", "Vehicle"),
            ("JournalEntryLine", "CostCenterId", "CostCenter"),
            ("MaterialRequisition", "ConvertedInvoiceId", "Invoice"),
            ("MaterialRequisition", "SupplierId", "Supplier"),
            ("MaterialRequisition", "WarehouseId", "Warehouse"),
            ("OnlinePayment", "VoucherId", "Voucher"),
            ("PosCashMovement", "JournalEntryId", "JournalEntry"),
            ("PosHeldCart", "CustomerId", "Customer"),
            ("PosOffer", "TargetCategoryId", "ProductCategory"),
            ("PosOffer", "TargetItemId", "Product"),
            ("PosSalesReturn", "CreditNoteInvoiceId", "Invoice"),
            ("PosSalesReturn", "CustomerId", "Customer"),
            ("PosSalesReturn", "OriginalInvoiceId", "Invoice"),
            ("PosShift", "VarianceJournalEntryId", "JournalEntry"),
            ("PosTransaction", "CustomerId", "Customer"),
            ("PosTransaction", "InvoiceId", "Invoice"),
            ("Quotation", "ConvertedInvoiceId", "Invoice"),
            ("StockMovement", "WarehouseId", "Warehouse"),
            ("SupportTicket", "CreatedByUserId", "User"),
            ("SupportTicketReply", "AuthorUserId", "User"),
            ("UserRolePermission", "UserId", "User"),
            ("Vehicle", "AgentId", "CarAgent"),
            ("Vehicle", "BrandId", "CarBrand"),
            ("Vehicle", "ModelId", "CarModel"),
            ("Vehicle", "ProcurementOrderId", "CarProcurementOrder"),
            ("Vehicle", "PurchaseInvoiceId", "Invoice"),
            ("Vehicle", "TrimId", "CarTrim"),
            ("Vehicle", "YearId", "CarYearModel"),
            ("Voucher", "JournalEntryId", "JournalEntry"),
        };

        /// <summary>
        /// تعبئة الأعمدة الجديدة من البيانات الحالية ثم تفريغ المراجع المعلّقة الاختيارية.
        /// الأعمدة الإلزامية (ItemId في البنود، ShiftId، DeliveryNoteId) لا تُعدَّل: إن وُجد فيها مرجع معلّق تفشل الترقية عمداً لمراجعته يدوياً.
        /// </summary>
        private static void PrepareData(MigrationBuilder mb)
        {
            foreach (var (table, purchaseSide) in PartyDocuments)
            {
                var hasPartyType = table != "Invoice";
                var supplierSide = hasPartyType
                    ? $"(CASE WHEN LOWER(d.[PartyType]) IN ('supplier','vendor') THEN 1 WHEN LOWER(d.[PartyType]) IN ('customer','client') THEN 0 WHEN {purchaseSide} THEN 1 ELSE 0 END)"
                    : $"(CASE WHEN {purchaseSide} THEN 1 ELSE 0 END)";
                mb.Sql($@"UPDATE d SET [CustomerId] = d.[PartyId] FROM [{table}] d
WHERE d.[PartyId] IS NOT NULL AND {supplierSide} = 0 AND EXISTS (SELECT 1 FROM [Customer] c WHERE c.[Id] = d.[PartyId]);");
                mb.Sql($@"UPDATE d SET [SupplierId] = d.[PartyId] FROM [{table}] d
WHERE d.[PartyId] IS NOT NULL AND {supplierSide} = 1 AND EXISTS (SELECT 1 FROM [Supplier] s WHERE s.[Id] = d.[PartyId]);");
            }

            mb.Sql(@"UPDATE i SET [ProductId] = i.[ItemId] FROM [InvoiceItem] i
WHERE i.[ItemId] <> '00000000-0000-0000-0000-000000000000' AND EXISTS (SELECT 1 FROM [Product] p WHERE p.[Id] = i.[ItemId]);");

            foreach (var (table, column, principal) in NullableReferences)
                mb.Sql($@"UPDATE t SET [{column}] = NULL FROM [{table}] t
WHERE t.[{column}] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [{principal}] p WHERE p.[Id] = t.[{column}]);");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Quotation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "Quotation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "InvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Agreement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "Agreement",
                type: "uniqueidentifier",
                nullable: true);

            PrepareData(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "IX_Voucher_JournalEntryId",
                table: "Voucher",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_AgentId",
                table: "Vehicle",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_BrandId",
                table: "Vehicle",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_ModelId",
                table: "Vehicle",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_ProcurementOrderId",
                table: "Vehicle",
                column: "ProcurementOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_PurchaseInvoiceId",
                table: "Vehicle",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_TrimId",
                table: "Vehicle",
                column: "TrimId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_YearId",
                table: "Vehicle",
                column: "YearId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRolePermission_UserId",
                table: "UserRolePermission",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketReply_AuthorUserId",
                table: "SupportTicketReply",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicket_CreatedByUserId",
                table: "SupportTicket",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_ItemId",
                table: "StockMovement",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_WarehouseId",
                table: "StockMovement",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItem_ItemId",
                table: "QuotationItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_ConvertedInvoiceId",
                table: "Quotation",
                column: "ConvertedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_CustomerId",
                table: "Quotation",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_SupplierId",
                table: "Quotation",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PosTransactionItem_ItemId",
                table: "PosTransactionItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PosTransaction_CustomerId",
                table: "PosTransaction",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PosTransaction_InvoiceId",
                table: "PosTransaction",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PosShift_VarianceJournalEntryId",
                table: "PosShift",
                column: "VarianceJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturnItem_ItemId",
                table: "PosSalesReturnItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturn_CreditNoteInvoiceId",
                table: "PosSalesReturn",
                column: "CreditNoteInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturn_CustomerId",
                table: "PosSalesReturn",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturn_OriginalInvoiceId",
                table: "PosSalesReturn",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturn_ShiftId",
                table: "PosSalesReturn",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOffer_TargetCategoryId",
                table: "PosOffer",
                column: "TargetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOffer_TargetItemId",
                table: "PosOffer",
                column: "TargetItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PosHeldCartItem_ItemId",
                table: "PosHeldCartItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PosHeldCart_CustomerId",
                table: "PosHeldCart",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovement_JournalEntryId",
                table: "PosCashMovement",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovement_ShiftId",
                table: "PosCashMovement",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayment_VoucherId",
                table: "OnlinePayment",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionItem_ItemId",
                table: "MaterialRequisitionItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_ConvertedInvoiceId",
                table: "MaterialRequisition",
                column: "ConvertedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_SupplierId",
                table: "MaterialRequisition",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_WarehouseId",
                table: "MaterialRequisition",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLine_CostCenterId",
                table: "JournalEntryLine",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleLine_BrandId",
                table: "InvoiceVehicleLine",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleLine_ModelId",
                table: "InvoiceVehicleLine",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleLine_TrimId",
                table: "InvoiceVehicleLine",
                column: "TrimId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleLine_VehicleId",
                table: "InvoiceVehicleLine",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItem_CostCenterId",
                table: "InvoiceItem",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItem_ProductId",
                table: "InvoiceItem",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_CustomerId",
                table: "Invoice",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_JournalEntryId",
                table: "Invoice",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_OriginalInvoiceId",
                table: "Invoice",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_SupplierId",
                table: "Invoice",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnNote_ContractId",
                table: "DeliveryReturnNote",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnNote_DeliveryNoteId",
                table: "DeliveryReturnNote",
                column: "DeliveryNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_ContractId",
                table: "DeliveryNote",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_CustomerId",
                table: "DeliveryNote",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_InvoiceId",
                table: "DeliveryNote",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_SupplierId",
                table: "DeliveryNote",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractMilestone_InvoiceId",
                table: "ContractMilestone",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrderItem_ItemId",
                table: "CommercialOrderItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_AgreementId",
                table: "CommercialOrder",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_ConvertedInvoiceId",
                table: "CommercialOrder",
                column: "ConvertedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_CustomerId",
                table: "CommercialOrder",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_SupplierId",
                table: "CommercialOrder",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_ItemId",
                table: "CommercialContractItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_CostCenterId",
                table: "CommercialContract",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_CustomerId",
                table: "CommercialContract",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_SupplierId",
                table: "CommercialContract",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CarSalesContract_CustomerId",
                table: "CarSalesContract",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CarSalesContract_FinancingBankId",
                table: "CarSalesContract",
                column: "FinancingBankId");

            migrationBuilder.CreateIndex(
                name: "IX_CarSalesContract_InvoiceId",
                table: "CarSalesContract",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarProcurementOrderVin_ItemId",
                table: "CarProcurementOrderVin",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CarProcurementOrderVin_VehicleId",
                table: "CarProcurementOrderVin",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_CarProcurementOrder_PurchaseInvoiceId",
                table: "CarProcurementOrder",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarModel_AgentId",
                table: "CarModel",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CarAgent_BrandId",
                table: "CarAgent",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_RequiredApproverUserId",
                table: "ApprovalRequest",
                column: "RequiredApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicyStep_ApproverUserId",
                table: "ApprovalPolicyStep",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppNotification_ApprovalRequestId",
                table: "AppNotification",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AgreementItem_ItemId",
                table: "AgreementItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Agreement_CustomerId",
                table: "Agreement",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Agreement_SupplierId",
                table: "Agreement",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agreement_Customer_CustomerId",
                table: "Agreement",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Agreement_Supplier_SupplierId",
                table: "Agreement",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgreementItem_Product_ItemId",
                table: "AgreementItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppNotification_ApprovalRequest_ApprovalRequestId",
                table: "AppNotification",
                column: "ApprovalRequestId",
                principalTable: "ApprovalRequest",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalPolicyStep_User_ApproverUserId",
                table: "ApprovalPolicyStep",
                column: "ApproverUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequest_User_RequiredApproverUserId",
                table: "ApprovalRequest",
                column: "RequiredApproverUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarAgent_CarBrand_BrandId",
                table: "CarAgent",
                column: "BrandId",
                principalTable: "CarBrand",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarModel_CarAgent_AgentId",
                table: "CarModel",
                column: "AgentId",
                principalTable: "CarAgent",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarProcurementOrder_Invoice_PurchaseInvoiceId",
                table: "CarProcurementOrder",
                column: "PurchaseInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarProcurementOrderVin_CarProcurementOrderItem_ItemId",
                table: "CarProcurementOrderVin",
                column: "ItemId",
                principalTable: "CarProcurementOrderItem",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarProcurementOrderVin_Vehicle_VehicleId",
                table: "CarProcurementOrderVin",
                column: "VehicleId",
                principalTable: "Vehicle",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarSalesContract_BankEntity_FinancingBankId",
                table: "CarSalesContract",
                column: "FinancingBankId",
                principalTable: "BankEntity",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarSalesContract_Customer_CustomerId",
                table: "CarSalesContract",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarSalesContract_Invoice_InvoiceId",
                table: "CarSalesContract",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialContract_CostCenter_CostCenterId",
                table: "CommercialContract",
                column: "CostCenterId",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialContract_Customer_CustomerId",
                table: "CommercialContract",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialContract_Supplier_SupplierId",
                table: "CommercialContract",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialContractItem_Product_ItemId",
                table: "CommercialContractItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_Agreement_AgreementId",
                table: "CommercialOrder",
                column: "AgreementId",
                principalTable: "Agreement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_Customer_CustomerId",
                table: "CommercialOrder",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_Invoice_ConvertedInvoiceId",
                table: "CommercialOrder",
                column: "ConvertedInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_Supplier_SupplierId",
                table: "CommercialOrder",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrderItem_Product_ItemId",
                table: "CommercialOrderItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContractMilestone_Invoice_InvoiceId",
                table: "ContractMilestone",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNote_CommercialContract_ContractId",
                table: "DeliveryNote",
                column: "ContractId",
                principalTable: "CommercialContract",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNote_Customer_CustomerId",
                table: "DeliveryNote",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNote_Invoice_InvoiceId",
                table: "DeliveryNote",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNote_Supplier_SupplierId",
                table: "DeliveryNote",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryReturnNote_CommercialContract_ContractId",
                table: "DeliveryReturnNote",
                column: "ContractId",
                principalTable: "CommercialContract",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryReturnNote_DeliveryNote_DeliveryNoteId",
                table: "DeliveryReturnNote",
                column: "DeliveryNoteId",
                principalTable: "DeliveryNote",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Customer_CustomerId",
                table: "Invoice",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Invoice_OriginalInvoiceId",
                table: "Invoice",
                column: "OriginalInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_JournalEntry_JournalEntryId",
                table: "Invoice",
                column: "JournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Supplier_SupplierId",
                table: "Invoice",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItem_CostCenter_CostCenterId",
                table: "InvoiceItem",
                column: "CostCenterId",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItem_Product_ProductId",
                table: "InvoiceItem",
                column: "ProductId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceVehicleLine_CarBrand_BrandId",
                table: "InvoiceVehicleLine",
                column: "BrandId",
                principalTable: "CarBrand",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceVehicleLine_CarModel_ModelId",
                table: "InvoiceVehicleLine",
                column: "ModelId",
                principalTable: "CarModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceVehicleLine_CarTrim_TrimId",
                table: "InvoiceVehicleLine",
                column: "TrimId",
                principalTable: "CarTrim",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceVehicleLine_Vehicle_VehicleId",
                table: "InvoiceVehicleLine",
                column: "VehicleId",
                principalTable: "Vehicle",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLine_CostCenter_CostCenterId",
                table: "JournalEntryLine",
                column: "CostCenterId",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_Invoice_ConvertedInvoiceId",
                table: "MaterialRequisition",
                column: "ConvertedInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_Supplier_SupplierId",
                table: "MaterialRequisition",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_Warehouse_WarehouseId",
                table: "MaterialRequisition",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisitionItem_Product_ItemId",
                table: "MaterialRequisitionItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OnlinePayment_Voucher_VoucherId",
                table: "OnlinePayment",
                column: "VoucherId",
                principalTable: "Voucher",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosCashMovement_JournalEntry_JournalEntryId",
                table: "PosCashMovement",
                column: "JournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosCashMovement_PosShift_ShiftId",
                table: "PosCashMovement",
                column: "ShiftId",
                principalTable: "PosShift",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosHeldCart_Customer_CustomerId",
                table: "PosHeldCart",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosHeldCartItem_Product_ItemId",
                table: "PosHeldCartItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosOffer_ProductCategory_TargetCategoryId",
                table: "PosOffer",
                column: "TargetCategoryId",
                principalTable: "ProductCategory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosOffer_Product_TargetItemId",
                table: "PosOffer",
                column: "TargetItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSalesReturn_Customer_CustomerId",
                table: "PosSalesReturn",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSalesReturn_Invoice_CreditNoteInvoiceId",
                table: "PosSalesReturn",
                column: "CreditNoteInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSalesReturn_Invoice_OriginalInvoiceId",
                table: "PosSalesReturn",
                column: "OriginalInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSalesReturn_PosShift_ShiftId",
                table: "PosSalesReturn",
                column: "ShiftId",
                principalTable: "PosShift",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosSalesReturnItem_Product_ItemId",
                table: "PosSalesReturnItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosShift_JournalEntry_VarianceJournalEntryId",
                table: "PosShift",
                column: "VarianceJournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosTransaction_Customer_CustomerId",
                table: "PosTransaction",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosTransaction_Invoice_InvoiceId",
                table: "PosTransaction",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosTransactionItem_Product_ItemId",
                table: "PosTransactionItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotation_Customer_CustomerId",
                table: "Quotation",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotation_Invoice_ConvertedInvoiceId",
                table: "Quotation",
                column: "ConvertedInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotation_Supplier_SupplierId",
                table: "Quotation",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItem_Product_ItemId",
                table: "QuotationItem",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovement_Product_ItemId",
                table: "StockMovement",
                column: "ItemId",
                principalTable: "Product",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovement_Warehouse_WarehouseId",
                table: "StockMovement",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicket_User_CreatedByUserId",
                table: "SupportTicket",
                column: "CreatedByUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketReply_User_AuthorUserId",
                table: "SupportTicketReply",
                column: "AuthorUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRolePermission_User_UserId",
                table: "UserRolePermission",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarAgent_AgentId",
                table: "Vehicle",
                column: "AgentId",
                principalTable: "CarAgent",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarBrand_BrandId",
                table: "Vehicle",
                column: "BrandId",
                principalTable: "CarBrand",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarModel_ModelId",
                table: "Vehicle",
                column: "ModelId",
                principalTable: "CarModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarProcurementOrder_ProcurementOrderId",
                table: "Vehicle",
                column: "ProcurementOrderId",
                principalTable: "CarProcurementOrder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarTrim_TrimId",
                table: "Vehicle",
                column: "TrimId",
                principalTable: "CarTrim",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_CarYearModel_YearId",
                table: "Vehicle",
                column: "YearId",
                principalTable: "CarYearModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicle_Invoice_PurchaseInvoiceId",
                table: "Vehicle",
                column: "PurchaseInvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Voucher_JournalEntry_JournalEntryId",
                table: "Voucher",
                column: "JournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agreement_Customer_CustomerId",
                table: "Agreement");

            migrationBuilder.DropForeignKey(
                name: "FK_Agreement_Supplier_SupplierId",
                table: "Agreement");

            migrationBuilder.DropForeignKey(
                name: "FK_AgreementItem_Product_ItemId",
                table: "AgreementItem");

            migrationBuilder.DropForeignKey(
                name: "FK_AppNotification_ApprovalRequest_ApprovalRequestId",
                table: "AppNotification");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalPolicyStep_User_ApproverUserId",
                table: "ApprovalPolicyStep");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequest_User_RequiredApproverUserId",
                table: "ApprovalRequest");

            migrationBuilder.DropForeignKey(
                name: "FK_CarAgent_CarBrand_BrandId",
                table: "CarAgent");

            migrationBuilder.DropForeignKey(
                name: "FK_CarModel_CarAgent_AgentId",
                table: "CarModel");

            migrationBuilder.DropForeignKey(
                name: "FK_CarProcurementOrder_Invoice_PurchaseInvoiceId",
                table: "CarProcurementOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CarProcurementOrderVin_CarProcurementOrderItem_ItemId",
                table: "CarProcurementOrderVin");

            migrationBuilder.DropForeignKey(
                name: "FK_CarProcurementOrderVin_Vehicle_VehicleId",
                table: "CarProcurementOrderVin");

            migrationBuilder.DropForeignKey(
                name: "FK_CarSalesContract_BankEntity_FinancingBankId",
                table: "CarSalesContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CarSalesContract_Customer_CustomerId",
                table: "CarSalesContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CarSalesContract_Invoice_InvoiceId",
                table: "CarSalesContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialContract_CostCenter_CostCenterId",
                table: "CommercialContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialContract_Customer_CustomerId",
                table: "CommercialContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialContract_Supplier_SupplierId",
                table: "CommercialContract");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialContractItem_Product_ItemId",
                table: "CommercialContractItem");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_Agreement_AgreementId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_Customer_CustomerId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_Invoice_ConvertedInvoiceId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_Supplier_SupplierId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrderItem_Product_ItemId",
                table: "CommercialOrderItem");

            migrationBuilder.DropForeignKey(
                name: "FK_ContractMilestone_Invoice_InvoiceId",
                table: "ContractMilestone");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNote_CommercialContract_ContractId",
                table: "DeliveryNote");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNote_Customer_CustomerId",
                table: "DeliveryNote");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNote_Invoice_InvoiceId",
                table: "DeliveryNote");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNote_Supplier_SupplierId",
                table: "DeliveryNote");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryReturnNote_CommercialContract_ContractId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryReturnNote_DeliveryNote_DeliveryNoteId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Customer_CustomerId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Invoice_OriginalInvoiceId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_JournalEntry_JournalEntryId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Supplier_SupplierId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItem_CostCenter_CostCenterId",
                table: "InvoiceItem");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItem_Product_ProductId",
                table: "InvoiceItem");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceVehicleLine_CarBrand_BrandId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceVehicleLine_CarModel_ModelId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceVehicleLine_CarTrim_TrimId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceVehicleLine_Vehicle_VehicleId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLine_CostCenter_CostCenterId",
                table: "JournalEntryLine");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_Invoice_ConvertedInvoiceId",
                table: "MaterialRequisition");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_Supplier_SupplierId",
                table: "MaterialRequisition");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_Warehouse_WarehouseId",
                table: "MaterialRequisition");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitionItem_Product_ItemId",
                table: "MaterialRequisitionItem");

            migrationBuilder.DropForeignKey(
                name: "FK_OnlinePayment_Voucher_VoucherId",
                table: "OnlinePayment");

            migrationBuilder.DropForeignKey(
                name: "FK_PosCashMovement_JournalEntry_JournalEntryId",
                table: "PosCashMovement");

            migrationBuilder.DropForeignKey(
                name: "FK_PosCashMovement_PosShift_ShiftId",
                table: "PosCashMovement");

            migrationBuilder.DropForeignKey(
                name: "FK_PosHeldCart_Customer_CustomerId",
                table: "PosHeldCart");

            migrationBuilder.DropForeignKey(
                name: "FK_PosHeldCartItem_Product_ItemId",
                table: "PosHeldCartItem");

            migrationBuilder.DropForeignKey(
                name: "FK_PosOffer_ProductCategory_TargetCategoryId",
                table: "PosOffer");

            migrationBuilder.DropForeignKey(
                name: "FK_PosOffer_Product_TargetItemId",
                table: "PosOffer");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSalesReturn_Customer_CustomerId",
                table: "PosSalesReturn");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSalesReturn_Invoice_CreditNoteInvoiceId",
                table: "PosSalesReturn");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSalesReturn_Invoice_OriginalInvoiceId",
                table: "PosSalesReturn");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSalesReturn_PosShift_ShiftId",
                table: "PosSalesReturn");

            migrationBuilder.DropForeignKey(
                name: "FK_PosSalesReturnItem_Product_ItemId",
                table: "PosSalesReturnItem");

            migrationBuilder.DropForeignKey(
                name: "FK_PosShift_JournalEntry_VarianceJournalEntryId",
                table: "PosShift");

            migrationBuilder.DropForeignKey(
                name: "FK_PosTransaction_Customer_CustomerId",
                table: "PosTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_PosTransaction_Invoice_InvoiceId",
                table: "PosTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_PosTransactionItem_Product_ItemId",
                table: "PosTransactionItem");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotation_Customer_CustomerId",
                table: "Quotation");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotation_Invoice_ConvertedInvoiceId",
                table: "Quotation");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotation_Supplier_SupplierId",
                table: "Quotation");

            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItem_Product_ItemId",
                table: "QuotationItem");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovement_Product_ItemId",
                table: "StockMovement");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovement_Warehouse_WarehouseId",
                table: "StockMovement");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicket_User_CreatedByUserId",
                table: "SupportTicket");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketReply_User_AuthorUserId",
                table: "SupportTicketReply");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRolePermission_User_UserId",
                table: "UserRolePermission");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarAgent_AgentId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarBrand_BrandId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarModel_ModelId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarProcurementOrder_ProcurementOrderId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarTrim_TrimId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_CarYearModel_YearId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicle_Invoice_PurchaseInvoiceId",
                table: "Vehicle");

            migrationBuilder.DropForeignKey(
                name: "FK_Voucher_JournalEntry_JournalEntryId",
                table: "Voucher");

            migrationBuilder.DropIndex(
                name: "IX_Voucher_JournalEntryId",
                table: "Voucher");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_AgentId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_BrandId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_ModelId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_ProcurementOrderId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_PurchaseInvoiceId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_TrimId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_YearId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_UserRolePermission_UserId",
                table: "UserRolePermission");

            migrationBuilder.DropIndex(
                name: "IX_SupportTicketReply_AuthorUserId",
                table: "SupportTicketReply");

            migrationBuilder.DropIndex(
                name: "IX_SupportTicket_CreatedByUserId",
                table: "SupportTicket");

            migrationBuilder.DropIndex(
                name: "IX_StockMovement_ItemId",
                table: "StockMovement");

            migrationBuilder.DropIndex(
                name: "IX_StockMovement_WarehouseId",
                table: "StockMovement");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItem_ItemId",
                table: "QuotationItem");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_ConvertedInvoiceId",
                table: "Quotation");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_CustomerId",
                table: "Quotation");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_SupplierId",
                table: "Quotation");

            migrationBuilder.DropIndex(
                name: "IX_PosTransactionItem_ItemId",
                table: "PosTransactionItem");

            migrationBuilder.DropIndex(
                name: "IX_PosTransaction_CustomerId",
                table: "PosTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PosTransaction_InvoiceId",
                table: "PosTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PosShift_VarianceJournalEntryId",
                table: "PosShift");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturnItem_ItemId",
                table: "PosSalesReturnItem");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturn_CreditNoteInvoiceId",
                table: "PosSalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturn_CustomerId",
                table: "PosSalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturn_OriginalInvoiceId",
                table: "PosSalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturn_ShiftId",
                table: "PosSalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_PosOffer_TargetCategoryId",
                table: "PosOffer");

            migrationBuilder.DropIndex(
                name: "IX_PosOffer_TargetItemId",
                table: "PosOffer");

            migrationBuilder.DropIndex(
                name: "IX_PosHeldCartItem_ItemId",
                table: "PosHeldCartItem");

            migrationBuilder.DropIndex(
                name: "IX_PosHeldCart_CustomerId",
                table: "PosHeldCart");

            migrationBuilder.DropIndex(
                name: "IX_PosCashMovement_JournalEntryId",
                table: "PosCashMovement");

            migrationBuilder.DropIndex(
                name: "IX_PosCashMovement_ShiftId",
                table: "PosCashMovement");

            migrationBuilder.DropIndex(
                name: "IX_OnlinePayment_VoucherId",
                table: "OnlinePayment");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisitionItem_ItemId",
                table: "MaterialRequisitionItem");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_ConvertedInvoiceId",
                table: "MaterialRequisition");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_SupplierId",
                table: "MaterialRequisition");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_WarehouseId",
                table: "MaterialRequisition");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLine_CostCenterId",
                table: "JournalEntryLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceVehicleLine_BrandId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceVehicleLine_ModelId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceVehicleLine_TrimId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceVehicleLine_VehicleId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItem_CostCenterId",
                table: "InvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItem_ProductId",
                table: "InvoiceItem");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_CustomerId",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_JournalEntryId",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_OriginalInvoiceId",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_SupplierId",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryReturnNote_ContractId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryReturnNote_DeliveryNoteId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_ContractId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_CustomerId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_InvoiceId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_SupplierId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_ContractMilestone_InvoiceId",
                table: "ContractMilestone");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrderItem_ItemId",
                table: "CommercialOrderItem");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_AgreementId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_ConvertedInvoiceId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_CustomerId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_SupplierId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContractItem_ItemId",
                table: "CommercialContractItem");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_CostCenterId",
                table: "CommercialContract");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_CustomerId",
                table: "CommercialContract");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_SupplierId",
                table: "CommercialContract");

            migrationBuilder.DropIndex(
                name: "IX_CarSalesContract_CustomerId",
                table: "CarSalesContract");

            migrationBuilder.DropIndex(
                name: "IX_CarSalesContract_FinancingBankId",
                table: "CarSalesContract");

            migrationBuilder.DropIndex(
                name: "IX_CarSalesContract_InvoiceId",
                table: "CarSalesContract");

            migrationBuilder.DropIndex(
                name: "IX_CarProcurementOrderVin_ItemId",
                table: "CarProcurementOrderVin");

            migrationBuilder.DropIndex(
                name: "IX_CarProcurementOrderVin_VehicleId",
                table: "CarProcurementOrderVin");

            migrationBuilder.DropIndex(
                name: "IX_CarProcurementOrder_PurchaseInvoiceId",
                table: "CarProcurementOrder");

            migrationBuilder.DropIndex(
                name: "IX_CarModel_AgentId",
                table: "CarModel");

            migrationBuilder.DropIndex(
                name: "IX_CarAgent_BrandId",
                table: "CarAgent");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_RequiredApproverUserId",
                table: "ApprovalRequest");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalPolicyStep_ApproverUserId",
                table: "ApprovalPolicyStep");

            migrationBuilder.DropIndex(
                name: "IX_AppNotification_ApprovalRequestId",
                table: "AppNotification");

            migrationBuilder.DropIndex(
                name: "IX_AgreementItem_ItemId",
                table: "AgreementItem");

            migrationBuilder.DropIndex(
                name: "IX_Agreement_CustomerId",
                table: "Agreement");

            migrationBuilder.DropIndex(
                name: "IX_Agreement_SupplierId",
                table: "Agreement");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Agreement");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Agreement");
        }
    }
}
