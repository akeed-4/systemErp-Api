using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitTradeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoicePaymentSplit_Invoice_InvoiceId",
                table: "InvoicePaymentSplit");

            migrationBuilder.DropForeignKey(
                name: "FK_VoucherAllocation_Invoice_InvoiceId",
                table: "VoucherAllocation");

            migrationBuilder.CreateTable(
                name: "PurchaseInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssueTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyVatNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCrNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyStreet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyBuildingNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyDistrict = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyPostalCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAdditionalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSplitPayment = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ItemsDiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoiceDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaQrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaUblXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaPih = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaValidationMessages = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SendToZatca = table.Column<bool>(type: "bit", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReturn = table.Column<bool>(type: "bit", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CogsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettlementAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoice_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssueTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyVatNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCrNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyStreet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyBuildingNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyDistrict = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyPostalCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAdditionalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSplitPayment = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ItemsDiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoiceDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaQrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaUblXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaPih = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaValidationMessages = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SendToZatca = table.Column<bool>(type: "bit", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReturn = table.Column<bool>(type: "bit", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CogsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettlementAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturn_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssueTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyVatNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCrNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyStreet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyBuildingNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyDistrict = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyPostalCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAdditionalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSplitPayment = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ItemsDiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoiceDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaQrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaUblXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaPih = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaValidationMessages = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SendToZatca = table.Column<bool>(type: "bit", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReturn = table.Column<bool>(type: "bit", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CogsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettlementAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoice_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssueTime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyVatNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCrNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyIdNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyStreet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyBuildingNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyDistrict = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyPostalCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyAdditionalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PartyCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSplitPayment = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ItemsDiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoiceDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZatcaHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaQrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaUblXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaPih = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZatcaValidationMessages = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SendToZatca = table.Column<bool>(type: "bit", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReturn = table.Column<bool>(type: "bit", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CogsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettlementAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturn_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmountOverride = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Standard"),
                    VatExemptionReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalBeforeVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItem_PurchaseInvoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "PurchaseInvoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmountOverride = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Standard"),
                    VatExemptionReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalBeforeVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItem_PurchaseReturn_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "PurchaseReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmountOverride = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Standard"),
                    VatExemptionReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalBeforeVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItem_SalesInvoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SalesInvoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmountOverride = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Standard"),
                    VatExemptionReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalBeforeVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturnItem_SalesReturn_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SalesReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_TenantId",
                table: "PurchaseInvoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_TenantId_InvoiceNumber",
                table: "PurchaseInvoice",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoice_WarehouseId",
                table: "PurchaseInvoice",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItem_InvoiceId",
                table: "PurchaseInvoiceItem",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItem_TenantId",
                table: "PurchaseInvoiceItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_TenantId",
                table: "PurchaseReturn",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_TenantId_InvoiceNumber",
                table: "PurchaseReturn",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_WarehouseId",
                table: "PurchaseReturn",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_InvoiceId",
                table: "PurchaseReturnItem",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_TenantId",
                table: "PurchaseReturnItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_TenantId",
                table: "SalesInvoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_TenantId_InvoiceNumber",
                table: "SalesInvoice",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_WarehouseId",
                table: "SalesInvoice",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_InvoiceId",
                table: "SalesInvoiceItem",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_TenantId",
                table: "SalesInvoiceItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturn_TenantId",
                table: "SalesReturn",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturn_TenantId_InvoiceNumber",
                table: "SalesReturn",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturn_WarehouseId",
                table: "SalesReturn",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItem_InvoiceId",
                table: "SalesReturnItem",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItem_TenantId",
                table: "SalesReturnItem",
                column: "TenantId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceItem");

            migrationBuilder.DropTable(
                name: "PurchaseReturnItem");

            migrationBuilder.DropTable(
                name: "SalesInvoiceItem");

            migrationBuilder.DropTable(
                name: "SalesReturnItem");

            migrationBuilder.DropTable(
                name: "PurchaseInvoice");

            migrationBuilder.DropTable(
                name: "PurchaseReturn");

            migrationBuilder.DropTable(
                name: "SalesInvoice");

            migrationBuilder.DropTable(
                name: "SalesReturn");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoicePaymentSplit_Invoice_InvoiceId",
                table: "InvoicePaymentSplit",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherAllocation_Invoice_InvoiceId",
                table: "VoucherAllocation",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
